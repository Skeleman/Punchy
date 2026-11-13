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
                    // (Behind it, one to either side: in front is its work, a forge's fire or a baker's hearth, where
                    // a stand ended up in the flames.)
                    var at = s.Position + right * (k == 0 ? 0.7f : -0.7f) - forward * 0.8f;
                    var uid = nextSpare++;
                    Add(uid, (byte)item, 1, at);
                    list.Add(((byte)item, uid));
                }
                sparesOf[s.Individual] = list;
                Debug.Log($"[take] {FolkJobs.Of(in s)} {s.Individual:X8} stocked its post: {string.Join(", ", list.ConvertAll(x => ItemTable.Items[x.Item1].Name))}");
            }
        }

        /// <summary>
        /// Zach 11-12 ("a finished sword on a rack"): spares stand at the post rather than lie on the floor, a long tool or
        /// weapon leaning on a rack, anything smaller on a little table. The stand is cosmetic, worked out on each peer from the
        /// spare itself (its uid, item and place, which every peer has), and stays when the spare is taken, so an empty
        /// rack shows what's missing. Kept by place (a restocked spare reuses its stand), for this session only.
        /// </summary>
        readonly Dictionary<int3, (float3 pos, float yaw, bool rack)> stands = new Dictionary<int3, (float3, float, bool)>();
        MaterialPropertyBlock standWood, standDark;
        static readonly Color StandWood = new Color(0.42f, 0.29f, 0.17f), StandDark = new Color(0.27f, 0.18f, 0.1f);

        /// <summary>A spare long enough to lean on a rack's crossbar (anything shorter goes on a little table).</summary>
        static bool OnRack(Mesh m) => m.bounds.size.z > 0.75f;

        /// <summary>Stands a just-drawn spare on its stand (noting the stand the first time): leaning on a rack's crossbar
        /// with its head up, or lying on a little table's top.</summary>
        void StandSpare(int uid, in WorldItem it, GameObject go, Mesh mesh)
        {
            var key = (int3)math.round(it.Pos * 4f);
            if (!stands.TryGetValue(key, out var stand))
                stands[key] = stand = (it.Pos, uid * 37 % 360, OnRack(mesh));
            var yaw = Quaternion.Euler(0f, stand.yaw, 0f);
            if (stand.rack)
            {
                // (Tipped 70 degrees up, its head toward the crossbar; its butt on the ground where the lean meets the bar.)
                const float Tip = 70f;
                var up = Mathf.Sin(Tip * Mathf.Deg2Rad);
                var back = Mathf.Cos(Tip * Mathf.Deg2Rad);
                var butt = mesh.bounds.min.z;
                var standOff = -(ItemMeshes.RackBar / up) * back - 0.03f;
                var local = new Vector3(0f, 0.02f - butt * up, standOff - butt * back);
                go.transform.SetPositionAndRotation((Vector3)stand.pos + yaw * local, yaw * Quaternion.Euler(-Tip, 0f, 0f));
            }
            else
                go.transform.SetPositionAndRotation((Vector3)stand.pos + Vector3.up * ItemMeshes.TableTop, yaw * Quaternion.Euler(0f, uid * 53 % 90, 0f));
        }

        /// <summary>Each frame: the stands within DrawRange of the camera.</summary>
        void DrawStands(Vector3 cam)
        {
            if (stands.Count == 0 || itemMaterial == null)
                return;
            if (standWood == null)
            {
                standWood = new MaterialPropertyBlock();
                standWood.SetColor(BaseColorId, StandWood);
                standDark = new MaterialPropertyBlock();
                standDark.SetColor(BaseColorId, StandDark);
            }
            foreach (var s in stands.Values)
            {
                if (((Vector3)s.pos - cam).sqrMagnitude > DrawRange * DrawRange)
                    continue;
                var mesh = s.rack ? ItemMeshes.Rack() : ItemMeshes.Table();
                var m = Matrix4x4.TRS(s.pos, Quaternion.Euler(0f, s.yaw, 0f), Vector3.one);
                Graphics.RenderMesh(new RenderParams(itemMaterial) { matProps = standWood }, mesh, 0, m);
                Graphics.RenderMesh(new RenderParams(itemMaterial) { matProps = standDark }, mesh, 1, m);
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
