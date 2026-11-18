using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-181 (#534): the way up a natural cave's shafts. Roots, vines or a rope hang down the shaft wall from the rim to
    /// the chamber floor, with an Assisted-grade ClimbLadder along them so a player can climb out. Placed after the
    /// hangings on hash stream 12, so no earlier draw moves; mines keep their baked ladders. Local like the wall details:
    /// CaveSystem.Climbs hands the ones near the camera to CaveClimbSystem.
    /// </summary>
    public partial class CaveWallBuild
    {
        public readonly List<ClimbLadder> Climbs = new List<ClimbLadder>();

        void PlaceClimbs(in TerrainSource source)
        {
            if (Site.Mine || Site.Tunnel > 0)
                return;
            ref var set = ref source.Caves.Value;
            var c = set.Caves[Cave];
            var kind = Site.Forest ? CaveHangingKind.Roots : Site.Rain > 0.6f ? CaveHangingKind.Vines : CaveHangingKind.Rope;
            for (int si = c.FirstSegment; si < c.FirstSegment + c.SegmentCount; si++)
            {
                var seg = set.Segments[si];
                if (set.Kinds[seg.A] == CaveNodeKind.Chamber && set.Kinds[seg.B] == CaveNodeKind.Shaft)
                    ShaftClimb(source, ref set, seg.A, seg.B, kind, CaveWallDetails.Hash(key, 12, si, 0));
                else if (set.Kinds[seg.B] == CaveNodeKind.Chamber && set.Kinds[seg.A] == CaveNodeKind.Shaft)
                    ShaftClimb(source, ref set, seg.B, seg.A, kind, CaveWallDetails.Hash(key, 12, si, 0));
            }
        }

        void ShaftClimb(in TerrainSource source, ref CaveSetBlob set, int chamber, int shaft, CaveHangingKind kind, uint h)
        {
            var down = new float3(0f, -1f, 0f);
            var centre = set.Nodes[chamber].Position;
            if (!Cast(source, centre, down, 12f, out var floorHit, out _))
                return;
            var axis = set.Nodes[shaft].Position.xz;
            var ground = source.SampleHeight(axis);
            var start = CaveWallDetails.U(h, 0) * math.PI * 2f;
            for (int tries = 0; tries < 6; tries++)
            {
                var a = start + tries * math.PI / 3f;
                var dir = new float3(math.cos(a), 0f, math.sin(a));
                // The nearest the wall comes to the axis up the shaft, so the strands never pass through rock.
                var reach = float.MaxValue;
                for (var y = floorHit.y + 2.5f; y < ground - 1.2f; y += 1f)
                {
                    var p = new float3(axis.x, y, axis.y);
                    if (!Cast(source, p, dir, 8f, out var hit, out _))
                    {
                        reach = 0f;
                        break;
                    }
                    reach = math.min(reach, math.distance(hit.xz, axis));
                }
                if (reach < 0.6f || reach == float.MaxValue)
                    continue;
                var at = new float3(axis.x, centre.y, axis.y) + dir * (reach - 0.3f);
                if (!Cast(source, at, down, 12f, out var foot, out _) || math.abs(foot.y - floorHit.y) > 1.5f)
                    continue;
                var lip = new float3(axis.x, 0f, axis.y) + dir * (reach + 0.15f);
                lip.y = source.SampleHeight(lip.xz);
                if (lip.y - foot.y < 3f)
                    continue;
                var across = new float3(-dir.z, 0f, dir.x);
                var strands = 2 + (int)(CaveWallDetails.U(h, 1) * 2f);
                for (int s = 0; s < strands; s++)
                {
                    var u = (s - (strands - 1) * 0.5f) * 0.22f;
                    var top = lip + across * u + new float3(0f, 0.05f, 0f);
                    var end = foot + across * u + new float3(0f, 0.05f, 0f);
                    Hangings.Add(new CaveHanging(kind, top, end, math.distance(top, end) * 1.01f, CaveWallDetails.U(h, 2 + s)));
                }
                Climbs.Add(new ClimbLadder
                {
                    Foot = foot, Top = new float3(foot.x, lip.y, foot.z), Out = -dir, Grade = Punchy.Fauna.Climbing.Assisted
                });
                return;
            }
        }
    }

    /// <summary>W-181: the cave climbs near the camera this frame, filled by CaveSystem.Climbs.</summary>
    public struct CaveClimbRoutes
    {
        public static readonly SharedStatic<FixedList512Bytes<ClimbLadder>> Near =
            SharedStatic<FixedList512Bytes<ClimbLadder>>.GetOrCreate<CaveClimbRoutes, NearKey>();
        public static readonly SharedStatic<int> Frame = SharedStatic<int>.GetOrCreate<CaveClimbRoutes, FrameKey>();
        class NearKey { }
        class FrameKey { }

        /// <summary>How far from the camera climbs are handed on (m): a third-person camera trails the player.</summary>
        public const float Gather = 20f;
    }

    /// <summary>W-181: adds the cave climbs to the world's ladders after ClimbLadderSystem refills them, before the player moves.</summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(Punchy.Fauna.ClimbLadderSystem))]
    [UpdateBefore(typeof(Punchy.Player.PlayerMovementSystem))]
    public partial struct CaveClimbSystem : ISystem
    {
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<Punchy.Player.PlayerInput>();

        public void OnUpdate(ref SystemState state)
        {
            // A stopped or missing CaveSystem leaves no stale climbs behind.
            if (CaveClimbRoutes.Frame.Data < UnityEngine.Time.frameCount - 1)
                return;
            ref var near = ref CaveClimbRoutes.Near.Data;
            ref var list = ref Punchy.Fauna.ClimbLadders.List.Data;
            for (int i = 0; i < near.Length && list.Length < list.Capacity; i++)
            {
                var seen = false;
                for (int k = 0; k < list.Length && !seen; k++)
                    seen = math.all(list[k].Foot == near[i].Foot) && math.all(list[k].Top == near[i].Top);
                if (!seen)
                    list.Add(near[i]);
            }
        }
    }
}
