using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    // Pockets flood or dry whole so deeper ground beside a wet bay is never left dry (F-22).
    public struct BayMaskBlob
    {
        public int Resolution;
        public int Sub;
        public float2 Origin;
        public float Step;
        public BlobArray<int> Block;
        public BlobArray<float> Level;

        public float At(int2 f)
        {
            var c = f / Sub;
            if (math.any(f < 0) || math.any(c >= Resolution)) return float.MinValue;
            var b = Block[c.y * Resolution + c.x];
            return b < 0 ? float.MinValue : Level[b + (f.y - c.y * Sub) * Sub + f.x - c.x * Sub];
        }

        public float Sample(float2 xz)
        {
            var f = (int2)math.floor((xz - Origin) / Step - 0.5f);
            return math.max(math.max(At(f), At(f + new int2(1, 0))), math.max(At(f + new int2(0, 1)), At(f + 1)));
        }
    }

    public partial struct TerrainSource
    {
        public BlobAssetReference<BayMaskBlob> BayMask;

        const float BayStep = 4f;
        const int BayReach = 4;

        public BlobAssetReference<BayMaskBlob> BuildBayMask(Unity.Collections.Allocator allocator, out string report)
        {
            report = "no rivers";
            if (!Rivers.IsCreated) return default;
            ref var map = ref Rivers.Value;
            int res = map.Resolution, sub = math.max(1, (int)math.round(map.CellSize / BayStep));
            float step = map.CellSize / sub;
            var lakeCell = new bool[res * res];
            for (int i = 0; i < lakeCell.Length; i++) lakeCell[i] = map.LakeDepth[i] > 0f;
            var block = new int[res * res];
            int blocks = 0;
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                block[z * res + x] = -1;
                for (int dz = -BayReach; dz <= BayReach && block[z * res + x] < 0; dz++)
                for (int dx = -BayReach; dx <= BayReach; dx++)
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= res || nz >= res || !lakeCell[nz * res + nx]) continue;
                    block[z * res + x] = blocks++;
                    break;
                }
            }
            var cells = new List<int>(blocks);
            for (int i = 0; i < block.Length; i++) if (block[i] >= 0) cells.Add(i);
            int per = sub * sub, n = blocks * per;
            var height = new float[n];
            var lakeLevel = new float[n];
            var self = this;
            var origin = map.Origin;
            BakeParallel.For(blocks, b =>
            {
                int c = cells[b], cx = c % res, cz = c / res;
                for (int k = 0; k < per; k++)
                {
                    var xz = origin + (new float2(cx * sub + k % sub, cz * sub + k / sub) + 0.5f) * step;
                    var h = self.SampleHeight(xz);
                    var cover = RiverSampling.Lake(ref self.Rivers.Value, xz, out var surface, out _);
                    height[b * per + k] = h;
                    lakeLevel[b * per + k] = cover > 0f && surface > h + 0.05f ? surface : float.MinValue;
                }
            });

            int Index(int2 f)
            {
                var c = f / sub;
                if (math.any(f < 0) || math.any(c >= res)) return -1;
                var b = block[c.y * res + c.x];
                return b < 0 ? -1 : b * per + (f.y - c.y * sub) * sub + f.x - c.x * sub;
            }
            int2 Fine(int i)
            {
                int c = cells[i / per], k = i % per;
                return new int2(c % res * sub + k % sub, c / res * sub + k / sub);
            }
            var dirs = new[] { new int2(1, 0), new int2(-1, 0), new int2(0, 1), new int2(0, -1) };

            var level = new float[n];
            var cap = new float[n];
            for (int i = 0; i < n; i++) cap[i] = float.MaxValue;
            var queue = new Queue<int>();
            var seen = new bool[n];
            var pocket = new List<int>();
            int kept = 0, dropped = 0, wet = 0;
            // A higher lake's flood can reach a lower lake's shore and swallow its pockets into one open pocket; capping the
            // dropped cells below that level and flooding again lets the lower lake keep its own closed pockets.
            for (int round = 0, open = 1; round < 8 && open > 0; round++)
            {
                open = 0;
                kept = wet = 0;
                for (int i = 0; i < n; i++)
                {
                    level[i] = lakeLevel[i];
                    seen[i] = false;
                    if (lakeLevel[i] > float.MinValue) queue.Enqueue(i);
                }
                while (queue.Count > 0)
                {
                    var i = queue.Dequeue();
                    var f = Fine(i);
                    foreach (var d in dirs)
                    {
                        var j = Index(f + d);
                        if (j < 0 || lakeLevel[j] > float.MinValue || height[j] + 0.05f >= level[i] || level[j] >= level[i] || level[i] >= cap[j]) continue;
                        level[j] = level[i];
                        queue.Enqueue(j);
                    }
                }
                for (int s = 0; s < n; s++)
                {
                    if (seen[s] || lakeLevel[s] > float.MinValue || level[s] == float.MinValue) continue;
                    pocket.Clear();
                    pocket.Add(s);
                    seen[s] = true;
                    var leaks = false;
                    for (int p = 0; p < pocket.Count; p++)
                    {
                        var f = Fine(pocket[p]);
                        foreach (var d in dirs)
                        {
                            var j = Index(f + d);
                            if (j < 0) { leaks = true; continue; }
                            if (seen[j] || lakeLevel[j] > float.MinValue || level[j] != level[s]) continue;
                            seen[j] = true;
                            pocket.Add(j);
                        }
                    }
                    if (leaks) { open++; foreach (var i in pocket) { cap[i] = math.min(cap[i], level[i]); level[i] = float.MinValue; } }
                    else { kept++; wet += pocket.Count; }
                }
                dropped += open;
            }

            var used = new int[blocks];
            int usedBlocks = 0;
            for (int b = 0; b < blocks; b++)
            {
                used[b] = -1;
                for (int k = 0; k < per; k++)
                    if (lakeLevel[b * per + k] == float.MinValue && level[b * per + k] > float.MinValue) { used[b] = usedBlocks++; break; }
            }
            report = $"{blocks} blocks near lakes, {kept} pockets kept ({wet} cells of {step:F0} m in {usedBlocks} blocks), {dropped} open pockets dropped";
            using var builder = new BlobBuilder(Unity.Collections.Allocator.Temp);
            ref var root = ref builder.ConstructRoot<BayMaskBlob>();
            root.Resolution = res;
            root.Sub = sub;
            root.Origin = origin;
            root.Step = step;
            var outBlock = builder.Allocate(ref root.Block, res * res);
            for (int i = 0; i < res * res; i++) outBlock[i] = block[i] < 0 ? -1 : used[block[i]] < 0 ? -1 : used[block[i]] * per;
            var outLevel = builder.Allocate(ref root.Level, usedBlocks * per);
            for (int b = 0; b < blocks; b++)
            {
                if (used[b] < 0) continue;
                for (int k = 0; k < per; k++)
                    outLevel[used[b] * per + k] = lakeLevel[b * per + k] > float.MinValue ? float.MinValue : level[b * per + k];
            }
            return builder.CreateBlobAssetReference<BayMaskBlob>(allocator);
        }

        float LooseBayLevel(float2 xz, float height, float water)
        {
            var shore = RiverSampling.ShoreLevel(ref Rivers.Value, xz);
            if (shore > height + 0.05f && shore > water
                && RiverSampling.NearestLakeAbove(ref Rivers.Value, xz, height + 0.05f, out var lakeCentre, out var bayLevel)
                && bayLevel - height <= BayDepthLimit(lakeCentre) && ConnectsBelow(xz, lakeCentre, bayLevel))
                return bayLevel;
            return float.MinValue;
        }
    }
}
