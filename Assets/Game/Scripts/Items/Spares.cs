using System.Collections.Generic;
using Punchy.Fauna;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Items
{
    /// <summary>
    /// Spare stock at the folk's posts (Zach 11-12, taking folk items row 2): what a worker keeps at its post and isn't
    /// using, laid out there as ordinary world items anyone can take with the Use key: a smith's spare hammer and a
    /// finished sword, a woodcutter's axe, a farmer's hoe, a baker's loaves, a healer's bowl, a trader's wares. The
    /// post's props (a kneading table, a chopping block, the well's rope: FolkKit.Post0/1) are its own and stay.
    /// The host (or solo) lays a worker's spares out the first time it's seen at work, beside it at its feet' height
    /// (so on a smithy's floor, not the ground under it), and sends them as any item that appears (ItemSpawnRpc;
    /// late joiners with the rest). Which spares are whose is kept (sparesOf), for the worker to notice one gone.
    /// Their uids are from SpareBase, apart from drops, so the drop limit never clears them.
    /// </summary>
    public partial class Items
    {
        const int SpareBase = 3_000_000;
        int nextSpare = SpareBase;
        /// <summary>Each stocked worker's spares (by Individual): the item and its uid, -1 once taken (host or solo).</summary>
        readonly Dictionary<uint, List<(byte item, int uid)>> sparesOf = new Dictionary<uint, List<(byte, int)>>();
        float spareScanAt;
        bool spareOnlyArg;

        /// <summary>A job's spares, the tools and wares it uses (FolkKits.Fill's), one or two; -1 for none.</summary>
        static (int, int) SparesFor(FolkJob job) => job switch
        {
            FolkJob.Smith => (ItemTable.SmithHammer, ItemTable.Sword),
            FolkJob.Woodcutter => (ItemTable.Axe, -1),
            FolkJob.Farmer => (ItemTable.Hoe, -1),
            FolkJob.Baker => (ItemTable.Bread, ItemTable.Bread),
            FolkJob.Healer => (ItemTable.Bowl, -1),
            FolkJob.Merchant or FolkJob.Trader => (ItemTable.Jar, ItemTable.Apple),
            FolkJob.Miner => (ItemTable.Pickaxe, -1),
            FolkJob.WellKeeper => (ItemTable.Bucket, -1),
            FolkJob.Fisher => (ItemTable.Rod, -1),
            _ => (-1, -1),
        };

        /// <summary>Once a second (host or solo): workers at their posts not yet stocked get their spares.</summary>
        void StockPosts()
        {
            if (Joined || Time.realtimeSinceStartup < spareScanAt)
                return;
            spareScanAt = Time.realtimeSinceStartup + 1f;
            // (The host's own simulation, not its client's copies: a copy's village job isn't sent, FolkJobs.Of.)
            var w = Hosting && server != null && server.IsCreated ? server : World.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated)
                return;
            using var states = Q(w.EntityManager, ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>()).ToComponentDataArray<CreatureState>(Allocator.Temp);
            foreach (var s in states)
            {
                if (s.Mode != CreatureMode.Work || s.Stage == 0 || sparesOf.ContainsKey(s.Individual))
                    continue;
                var (a, b) = SparesFor(FolkJobs.Of(in s));
                if (a < 0)
                    continue;
                var forward = new float3(math.sin(s.Yaw), 0f, math.cos(s.Yaw));
                var right = new float3(forward.z, 0f, -forward.x);
                var list = new List<(byte, int)>();
                for (int k = 0; k < 2; k++)
                {
                    var item = k == 0 ? a : b;
                    if (item < 0)
                        continue;
                    // (Beside it and a little ahead, one either side, where it can reach them.)
                    var at = s.Position + right * (k == 0 ? 0.8f : -0.8f) + forward * 0.5f;
                    var uid = nextSpare++;
                    Add(uid, (byte)item, 1, at);
                    list.Add(((byte)item, uid));
                }
                sparesOf[s.Individual] = list;
                Debug.Log($"[take] {FolkJobs.Of(in s)} {s.Individual:X8} stocked its post: {string.Join(", ", list.ConvertAll(x => ItemTable.Items[x.Item1].Name))}");
            }
        }

        /// <summary>A spare taken (host or solo): marked gone from its worker's stock, for the worker to notice.</summary>
        void NoteSpareTaken(int uid)
        {
            if (uid < SpareBase)
                return;
            foreach (var kv in sparesOf)
                for (int i = 0; i < kv.Value.Count; i++)
                    if (kv.Value[i].uid == uid)
                    {
                        kv.Value[i] = (kv.Value[i].item, -1);
                        Debug.Log($"[take] {kv.Key:X8}'s spare {ItemTable.Items[kv.Value[i].item].Name} taken");
                        return;
                    }
        }
    }
}
