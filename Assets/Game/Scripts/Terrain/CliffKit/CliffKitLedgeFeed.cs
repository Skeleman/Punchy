using Punchy.Fauna;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Punchy.Terrain
{
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(Punchy.Player.PlayerMovementSystem))]
    public partial struct CliffKitLedgeFeedSystem : ISystem
    {
        public const float Gather = 6f; // gameplay
        const float DepthMax = 2f, DepthStep = 0.4f; // gameplay
        static readonly SharedStatic<FixedList512Bytes<ClimbLedge>> Mine = SharedStatic<FixedList512Bytes<ClimbLedge>>.GetOrCreate<CliffKitLedgeFeedSystem, MineKey>();
        class MineKey { }
        static float3 DeadEnd => new float3(0f, -10000f, 0f);

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Punchy.Player.PlayerInput>();
            state.RequireForUpdate<TerrainSource>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var source = SystemAPI.GetSingleton<TerrainSource>();
            ref var list = ref ClimbLedges.List.Data;
            ref var mine = ref Mine.Data;
            var foreign = 0;
            for (int s = 0; s < list.Length; s++)
                if (!IsMine(list[s], ref mine))
                    foreign++;
            var wanted = new FixedList512Bytes<ClimbLedge>();
            if (source.CliffKit.IsCreated)
            {
                var found = new FixedList512Bytes<int>();
                var dist = new FixedList512Bytes<float>();
                foreach (var t in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<Punchy.Player.PlayerTag>())
                    Near(ref source.CliffKit.Value, t.ValueRO.Position, ref found, ref dist);
                foreach (var t in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<Punchy.Net.NetPlayerLook>())
                    Near(ref source.CliffKit.Value, t.ValueRO.Position, ref found, ref dist);
                Pick(ref source.CliffKit.Value, source, ref list, ref found, ref dist, list.Capacity - foreign, ref wanted);
            }
            Apply(ref list, ref mine, ref wanted);
        }

        static bool Same(in ClimbLedge a, in ClimbLedge b) => math.all(a.From == b.From) && math.all(a.To == b.To);

        static bool IsMine(in ClimbLedge lip, ref FixedList512Bytes<ClimbLedge> mine)
        {
            if (lip.Grade == 255 && math.all(lip.From == DeadEnd) && math.all(lip.To == DeadEnd))
                return true;
            for (int m = 0; m < mine.Length; m++)
                if (Same(mine[m], lip))
                    return true;
            return false;
        }

        // A hang holds its slot's index, so kept lips never move and dropped ones leave a dead slot.
        static void Apply(ref FixedList512Bytes<ClimbLedge> list, ref FixedList512Bytes<ClimbLedge> mine, ref FixedList512Bytes<ClimbLedge> wanted)
        {
            var fresh = wanted;
            for (int s = 0; s < list.Length; s++)
            {
                if (!IsMine(list[s], ref mine))
                    continue;
                var kept = false;
                for (int w = 0; w < fresh.Length && !kept; w++)
                    if (Same(fresh[w], list[s]))
                    {
                        fresh.RemoveAtSwapBack(w);
                        kept = true;
                    }
                if (!kept)
                    list[s] = new ClimbLedge { From = DeadEnd, To = DeadEnd, Out = new float2(0f, 1f), Grade = 255 };
            }
            for (int s = 0; s < list.Length && fresh.Length > 0; s++)
                if (list[s].Grade == 255 && math.all(list[s].From == DeadEnd))
                {
                    list[s] = fresh[fresh.Length - 1];
                    fresh.RemoveAt(fresh.Length - 1);
                }
            while (fresh.Length > 0 && list.Length < list.Capacity)
            {
                list.Add(fresh[fresh.Length - 1]);
                fresh.RemoveAt(fresh.Length - 1);
            }
            while (list.Length > 0 && list[list.Length - 1].Grade == 255 && math.all(list[list.Length - 1].From == DeadEnd))
                list.RemoveAt(list.Length - 1);
            mine = wanted;
        }

        static void Near(ref CliffKitSetBlob set, float3 p, ref FixedList512Bytes<int> found, ref FixedList512Bytes<float> dist)
        {
            if (!math.all(math.isfinite(p)))
                return;
            var c0 = math.max((int2)math.floor((p.xz - Gather - set.Origin) / set.CellSize), 0);
            var c1 = math.min((int2)math.floor((p.xz + Gather - set.Origin) / set.CellSize), set.GridSize - 1);
            for (int cy = c0.y; cy <= c1.y; cy++)
                for (int cx = c0.x; cx <= c1.x; cx++)
                {
                    var range = set.Cells[cy * set.GridSize.x + cx];
                    for (int i = range.x; i < range.x + range.y; i++)
                    {
                        var index = set.Items[i];
                        if (math.distancesq(p.xz, set.Pieces[index].Position.xz) > math.square(set.Pieces[index].Reach + Gather))
                            continue;
                        var ledges = set.PieceLedges[index];
                        for (int l = ledges.x; l < ledges.x + ledges.y; l++)
                        {
                            var e = set.Ledges[l];
                            var ab = e.B - e.A;
                            var q = e.A + ab * math.saturate(math.dot(p - e.A, ab) / math.max(math.lengthsq(ab), 1e-6f));
                            var d = math.distance(p, q);
                            if (d > Gather)
                                continue;
                            var k = found.IndexOf(l);
                            if (k >= 0)
                                dist[k] = math.min(dist[k], d);
                            else if (found.Length < found.Capacity)
                            {
                                found.Add(l);
                                dist.Add(d);
                            }
                        }
                    }
                }
        }

        static void Pick(ref CliffKitSetBlob set, in TerrainSource source, ref FixedList512Bytes<ClimbLedge> list, ref FixedList512Bytes<int> found,
                         ref FixedList512Bytes<float> dist, int cap, ref FixedList512Bytes<ClimbLedge> wanted)
        {
            while (found.Length > 0 && wanted.Length < math.min(cap, wanted.Capacity))
            {
                var best = 0;
                for (int k = 1; k < found.Length; k++)
                    if (dist[k] < dist[best])
                        best = k;
                var e = set.Ledges[found[best]];
                found.RemoveAtSwapBack(best);
                dist.RemoveAtSwapBack(best);
                var lip = new ClimbLedge { From = e.A, To = e.B, Out = math.normalizesafe(e.Out.xz, new float2(0f, 1f)), Grade = e.Grade, Depth = -1f };
                for (int s = 0; s < list.Length; s++)
                    if (Same(list[s], lip))
                        lip.Depth = list[s].Depth;
                if (lip.Depth < 0f)
                    lip.Depth = Depth(source, lip, set.Pieces[e.Piece].Face == CliffKit.CaveFace);
                wanted.Add(lip);
            }
        }

        static float Depth(in TerrainSource source, in ClimbLedge lip, bool cave)
        {
            var mid = (lip.From + lip.To) * 0.5f;
            var depth = 0f;
            for (var back = DepthStep; back <= DepthMax + 1e-3f; back += DepthStep)
            {
                var q = new float3(mid.x - lip.Out.x * back, mid.y + 0.3f, mid.z - lip.Out.y * back);
                var deck = -1e9f;
                var on = cave ? (deck = CliffKit.CaveDeck(source, q, -1e9f)) > -1e8f : CliffKit.Deck(source, q, out deck);
                if (!on || math.abs(deck - mid.y) > 0.4f)
                    break;
                depth = back;
            }
            return depth;
        }
    }
}
