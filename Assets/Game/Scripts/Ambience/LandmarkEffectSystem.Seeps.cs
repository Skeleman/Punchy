using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;
using Kit = Punchy.Rendering.Effects.PlumeSystem;

namespace Punchy.Ambience
{
    /// <summary>
    /// W-142: lava seeping from rock, glowing beads that swell at a lip and slide down the face, on steep rock in vent
    /// biomes and on the walls of lava tubes and round cave lava pools. Spots hash world cells and pools, so nothing is
    /// placed; a spot says what seeps (SeepKind), so a water trickle can take the same spots later.
    /// </summary>
    public partial class LandmarkEffectSystem
    {
        public enum SeepKind : byte { Lava }

        public struct SeepSpot
        {
            public float3 Lip;
            public float2 Run;
            public float Drop;
            public float Width;
            public SeepKind Kind;
            public uint Hash;
        }

        const float SeepCell = 10f;
        const float SeepShare = 0.3f;
        const float SeepSteep = 0.9f;
        const float SeepSize = 2.5f;
        const float SeepMaxRange = 160f;
        const int MaxSeeps = 40;
        const int PoolSeeps = 5;
        const float TubeSeepShare = 0.35f;
        static readonly float4 LavaBead = new float4(1f, 0.32f, 0.05f, 3f);
        static readonly Vector4 SeepLight = new Vector4(2.4f, 0.65f, 0.06f, 4f);

        const int SeepLights = 6;

        public static int LastSeeps;

        readonly object seepOwner = new object();
        readonly List<SeepSpot> seeps = new List<SeepSpot>();
        bool[] lavaStyle = System.Array.Empty<bool>();
        readonly List<Kit.Plume> seepBeads = new List<Kit.Plume>();
        int2 seepCellAt = new int2(int.MinValue);
        float seepRangeAt;

        void Seeps(in TerrainSource source, Vector3 eye)
        {
            var gone = Kit.Sparks.Effect != null ? Kit.Sparks.Effect.GonePixels : 16.8f;
            var range = math.min(Punchy.Settings.DetailPolicy.ExtraReach(SeepSize, gone), SeepMaxRange);
            var cell = (int2)math.floor(((float3)eye).xz / SeepCell);
            if (!math.all(cell == seepCellAt) || math.abs(range - seepRangeAt) >= SeepCell)
            {
                seepCellAt = cell;
                seepRangeAt = range;
                FindSeeps(source, eye, range, seeps);
                seepBeads.Clear();
                foreach (var s in seeps)
                    seepBeads.Add(SeepPlume(s));
                LastSeeps = seeps.Count;
                Kit.Sparks.Set(seepOwner, seepBeads);
            }
            var t = Time.time;
            // Only the nearest few light their rock: forty seeps on a cone would starve the campfires' slots.
            for (int i = 0; i < math.min(seeps.Count, SeepLights); i++)
            {
                var s = seeps[i];
                var light = SeepLight * (0.8f + 0.2f * Mathf.Sin(t * 0.7f + (s.Hash & 255)));
                light.w = SeepLight.w * (0.8f + 0.4f * s.Width);
                Punchy.Settings.LightBudget.AddStill(Punchy.Settings.LightKind.Fire, (Vector3)(s.Lip - new float3(0f, s.Drop * 0.4f, 0f)), light);
            }
        }

        void FindSeeps(in TerrainSource source, Vector3 eye, float range, List<SeepSpot> into)
        {
            into.Clear();
            var at = ((float3)eye).xz;
            var n = (int)math.ceil(range / SeepCell);
            for (int z = -n; z <= n; z++)
            for (int x = -n; x <= n; x++)
            {
                var c = seepCellAt + new int2(x, z);
                var h = Scramble(math.hash(new int3(c, 0x5EE9)));
                if ((h & 0xFFFF) / 65536f >= SeepShare)
                    continue;
                var r = Scramble(h ^ 0x9E3779B9u);
                var xz = ((float2)c + new float2((r & 0xFFFF) / 65536f, (r >> 16) / 65536f)) * SeepCell;
                if (math.distancesq(xz, at) > range * range)
                    continue;
                if (SurfaceSeep(source, xz, Scramble(r), out var spot))
                    into.Add(spot);
            }
            if (source.Caves.IsCreated)
                CaveSeeps(source, (float3)eye, range, into);
            if (into.Count > MaxSeeps)
            {
                into.Sort((a, b) => math.distancesq(a.Lip, (float3)eye).CompareTo(math.distancesq(b.Lip, (float3)eye)));
                into.RemoveRange(MaxSeeps, into.Count - MaxSeeps);
            }
        }

        bool SurfaceSeep(in TerrainSource source, float2 xz, uint h, out SeepSpot spot)
        {
            spot = default;
            var ground = source.SampleGround(xz);
            if (!VentBiome(ground.Biomes) || ground.HasWater && ground.WaterSurface > ground.Height - 0.5f)
                return false;
            const float e = 0.75f;
            var grad = new float2(source.SampleHeight(xz + new float2(e, 0f)) - source.SampleHeight(xz - new float2(e, 0f)),
                                  source.SampleHeight(xz + new float2(0f, e)) - source.SampleHeight(xz - new float2(0f, e))) / (2f * e);
            var slope = math.length(grad);
            if (slope < SeepSteep)
                return false;
            var down = -grad / slope;
            var drop = 1.2f + 2.3f * ((h & 0xFF) / 255f);
            var foot = xz + down * (drop / slope);
            // (A face that bends away under the lip would leave the beads in the air: shorter, or none.)
            if (math.abs(source.SampleHeight(foot) - (ground.Height - drop)) > 0.3f * drop)
            {
                drop *= 0.5f;
                foot = xz + down * (drop / slope);
                if (math.abs(source.SampleHeight(foot) - (ground.Height - drop)) > 0.3f * drop)
                    return false;
            }
            var lift = 0.08f * math.sqrt(1f + slope * slope);
            spot = new SeepSpot
            {
                Lip = new float3(xz.x, ground.Height + lift, xz.y),
                Run = down / slope,
                Drop = drop,
                Width = 0.3f + 0.5f * (((h >> 8) & 0xFF) / 255f),
                Kind = SeepKind.Lava,
                Hash = h
            };
            return true;
        }

        void CaveSeeps(in TerrainSource source, float3 eye, float range, List<SeepSpot> into)
        {
            ref var set = ref source.Caves.Value;
            for (int c = 0; c < set.Caves.Length; c++)
            {
                ref var cave = ref set.Caves[c];
                if (cave.Style <= 0 || cave.Style > lavaStyle.Length || !lavaStyle[cave.Style - 1])
                    continue;
                if (math.any(eye + range < cave.Min) || math.any(eye - range > cave.Max))
                    continue;
                for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
                {
                    var node = set.Nodes[n];
                    if (math.distance(node.Position, eye) > range + node.Radius)
                        continue;
                    var h = Scramble(math.hash(new int2(n, 0x5EE9)));
                    if ((h & 0xFFFF) / 65536f >= TubeSeepShare)
                        continue;
                    var next = set.Nodes[n + 1 < cave.FirstNode + cave.NodeCount ? n + 1 : math.max(n - 1, cave.FirstNode)].Position;
                    var along = math.normalizesafe((next - node.Position).xz, new float2(1f, 0f));
                    var dir = ((h >> 16) & 1u) == 0u ? new float2(-along.y, along.x) : new float2(along.y, -along.x);
                    var y = node.Position.y + node.Radius * (0.1f + 0.4f * (((h >> 17) & 0xFF) / 255f));
                    if (WallSeep(source, new float3(node.Position.x, y, node.Position.z), dir, 0f, node.Radius * 3f, node.Position.y - node.Radius * 2f, Scramble(h), out var spot) &&
                        math.distance(spot.Lip, eye) <= range)
                        into.Add(spot);
                }
                for (int i = cave.FirstPool; i < cave.FirstPool + cave.PoolCount; i++)
                {
                    var pool = set.Pools[i];
                    if (pool.Lava <= 0.5f || math.distance(pool.Centre, eye) > range + pool.Radius + 20f)
                        continue;
                    for (int k = 0; k < PoolSeeps; k++)
                    {
                        var h = Scramble(math.hash(new int2(i, k + 0x5EE9)));
                        var angle = (h & 0xFFFF) / 65536f * 6.2832f;
                        var y = pool.Centre.y + 1.2f + 2.3f * (((h >> 16) & 0xFF) / 255f);
                        if (WallSeep(source, new float3(pool.Centre.x, y, pool.Centre.z), new float2(math.cos(angle), math.sin(angle)), pool.Radius * 0.5f, pool.Radius + 25f, pool.Centre.y, Scramble(h), out var spot) &&
                            math.distance(spot.Lip, eye) <= range)
                            into.Add(spot);
                    }
                }
            }
        }

        /// <summary>A seep on the first cave wall out from <paramref name="from"/> along dir, running down to the floor or no lower than <paramref name="lowest"/>.</summary>
        static bool WallSeep(in TerrainSource source, float3 from, float2 dir, float near, float far, float lowest, uint h, out SeepSpot spot)
        {
            spot = default;
            var y = from.y;
            if (!WallAt(source, from.xz, dir, y, near, far, out var lipR))
                return false;
            var floor = y;
            var probe = from.xz + dir * math.max(lipR - 0.3f, 0f);
            while (floor > lowest && source.CaveDistance(new float3(probe.x, floor - 0.25f, probe.y)) < 0f)
                floor -= 0.25f;
            var drop = math.min(y - math.max(floor, lowest), 3.5f);
            if (drop < 0.6f || !WallAt(source, from.xz, dir, y - drop + 0.1f, math.max(lipR - 2f, 0f), lipR + 2f, out var footR))
                return false;
            var lean = (footR - lipR) / drop;
            if (lean < -1.2f || lean > 0.3f)
                return false;
            var lip = from.xz + dir * (lipR - 0.06f);
            spot = new SeepSpot
            {
                Lip = new float3(lip.x, y, lip.y),
                Run = dir * math.min(lean, 0f) - dir * 0.02f,
                Drop = drop,
                Width = 0.3f + 0.4f * ((h & 0xFF) / 255f),
                Kind = SeepKind.Lava,
                Hash = h
            };
            return true;
        }

        /// <summary>How far out from a centre (along dir, at height y) the first cave rock is, between near and far.</summary>
        static bool WallAt(in TerrainSource source, float2 centre, float2 dir, float y, float near, float far, out float at)
        {
            const float step = 0.4f;
            at = near;
            var start = centre + dir * near;
            if (source.CaveDistance(new float3(start.x, y, start.y)) >= 0f)
                return false;
            for (var r = near + step; r <= far; r += step)
            {
                var p = centre + dir * r;
                if (source.CaveDistance(new float3(p.x, y, p.y)) < 0f)
                    continue;
                float air = r - step, rock = r;
                for (int k = 0; k < 4; k++)
                {
                    var mid = (air + rock) * 0.5f;
                    var q = centre + dir * mid;
                    if (source.CaveDistance(new float3(q.x, y, q.y)) < 0f)
                        air = mid;
                    else
                        rock = mid;
                }
                at = air;
                return true;
            }
            return false;
        }

        static Kit.Plume SeepPlume(in SeepSpot s)
        {
            var a = (s.Hash & 0xFF) / 255f;
            var b = ((s.Hash >> 8) & 0xFF) / 255f;
            return new Kit.Plume
            {
                Position = s.Lip,
                Seed = ((s.Hash >> 12) & 0x3FFF) / 16384f,
                Color = LavaBead,
                Shape = new float4(0f, s.Width, 0.05f + 0.03f * a, 7f + 6f * b),
                Timing = new float4(0f, 1f, 1f, 3 + (int)((s.Hash >> 20) % 3u)),
                Out = math.any(s.Run != 0f) ? s.Run : new float2(0.001f, 0f),
                Drop = s.Drop
            };
        }

        void SeepTables(BiomePalette palette)
        {
            var styles = CaveStyleTable.Collect(palette.Landmarks);
            lavaStyle = new bool[styles.Count];
            for (int i = 0; i < styles.Count; i++)
                lavaStyle[i] = styles[i] != null && styles[i].LavaPools;
            seepCellAt = new int2(int.MinValue);
        }

        void ClearSeeps()
        {
            Kit.Sparks.Set(seepOwner, null);
            seeps.Clear();
            seepCellAt = new int2(int.MinValue);
        }
    }
}
