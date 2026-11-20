using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Kit = Punchy.Rendering.Effects.PlumeSystem;

namespace Punchy.Ambience
{
    /// <summary>W-142 seeps hash world cells and cave pools so nothing is placed; SeepKind lets a water trickle reuse the spots.</summary>
    public partial class LandmarkEffectSystem
    {
        public enum SeepKind : byte { Lava, Water }

        public struct SeepSpot
        {
            public float3 Lip;
            public float2 Run;
            public float Drop;
            public float Width;
            public SeepKind Kind;
            public uint Hash;
            public bool UnderRock;
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
        readonly List<Kit.Plume> seepBeads = new List<Kit.Plume>();
        int2 seepCellAt = new int2(int.MinValue);
        float seepRangeAt;

        void Seeps(in TerrainSource source, Vector3 eye)
        {
            var gone = Kit.Sparks.Effect != null ? Kit.Sparks.Effect.GonePixels : 16.8f;
            var range = math.min(Punchy.Settings.DetailPolicy.ExtraReach(SeepSize, gone), SeepMaxRange);
            var cell = (int2)math.floor(((float3)eye).xz / SeepCell);
            if (seepSearching)
            {
                if (seepJob.IsCompleted)
                    EndSeepSearch(source);
            }
            else if (!math.all(cell == seepCellAt) || math.abs(range - seepRangeAt) >= SeepCell)
            {
                seepCellAt = cell;
                seepRangeAt = range;
                BeginSeepSearch(source, eye, range, cell);
            }
            DrawSeepFalls();
            var t = Time.time;
            // Only the nearest few light their rock: forty seeps on a cone would starve the campfires' slots.
            for (int i = 0, lit = 0; i < seeps.Count && lit < SeepLights; i++)
            {
                var s = seeps[i];
                if (s.Kind != SeepKind.Lava)
                    continue;
                lit++;
                var light = SeepLight * (0.8f + 0.2f * Mathf.Sin(t * 0.7f + (s.Hash & 255)));
                light.w = SeepLight.w * (0.8f + 0.4f * s.Width);
                Punchy.Settings.LightBudget.AddStill(Punchy.Settings.LightKind.Fire, (Vector3)(s.Lip - new float3(0f, s.Drop * 0.4f, 0f)), light);
            }
        }

        static bool WallSeep(in TerrainSource source, float3 from, float2 dir, float near, float far, float lowest, SeepKind kind, uint h, out SeepSpot spot)
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
                Width = (0.3f + 0.4f * ((h & 0xFF) / 255f)) * (kind == SeepKind.Lava ? 1f : 0.5f),
                Kind = kind,
                Hash = h,
                UnderRock = true
            };
            return true;
        }

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
                Timing = new float4(0f, 1f, 1f, 2 + (int)((s.Hash >> 20) % 2u)),
                Out = math.any(s.Run != 0f) ? s.Run : new float2(0.001f, 0f),
                Drop = s.Drop
            };
        }

        void SeepTables(BiomePalette palette)
        {
            DisposeSeepSearch();
            var styles = CaveStyleTable.Collect(palette.Landmarks);
            lavaStyle = new NativeArray<bool>(styles.Count, Allocator.Persistent);
            for (int i = 0; i < styles.Count; i++)
                lavaStyle[i] = styles[i] != null && styles[i].LavaPools;
            seepVents = new NativeArray<bool>(ventBiome, Allocator.Persistent);
            WaterSeepTables(palette);
            seepCellAt = new int2(int.MinValue);
        }

        void ClearSeeps()
        {
            if (seepSearching)
            {
                seepJob.Complete();
                seepSearching = false;
            }
            Kit.Sparks.Set(seepOwner, null);
            seeps.Clear();
            ClearSeepFalls();
            ClearSeepStreaks();
            seepCellAt = new int2(int.MinValue);
        }
    }
}
