using System.Collections.Generic;
using Punchy.Flora;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>The small life round the listener that makes the countryside's sounds (AmbientLife).</summary>
    public enum AmbientKind : byte { Songbird, Woodpecker, Owl, Cricket, Grasshopper, Frog, Cicada }

    /// <summary>One of them as it is this moment: where, which way, whether it's in the air, and its call count.</summary>
    public struct Ambient
    {
        public AmbientKind Kind;
        public uint Id;
        public float3 Position;
        public float Yaw;
        /// <summary>0 settled, up to 1 at the top of a hop or in a flit (wings out).</summary>
        public float Air;
        /// <summary>Its head's turn (an owl watching someone), radians.</summary>
        public float Head;
        public float4 Colour;
        /// <summary>How many calls it has made: CreatureAudioSystem plays one each time this goes up.</summary>
        public int Calls;
        /// <summary>Out of sight: a frog that plopped into the water, a cricket that's hopped off.</summary>
        public bool Hidden;
        /// <summary>The foot of its tree, for those that live in one.</summary>
        public float3 Tree;
    }

    /// <summary>
    /// The small life that makes the countryside's sounds, where it is, so nothing is heard without something there to
    /// find: songbirds in the trees' crowns by day (hopping from perch to perch, now and then down to the ground and
    /// back), a woodpecker on a trunk now and then, owls perched at night (their heads turning to watch whoever's near),
    /// crickets in the grass at night and grasshoppers by day, frogs at the water's edge at night (plopping in when
    /// someone comes close), cicadas in the trees on hot days.
    /// <para>
    /// Drawn only (CreatureRenderSystem) and heard only (CreatureAudioSystem), never simulated or sent: each 16 m cell
    /// near the listener is worked out once from the seed (which trees, where on the shore), and everything moves and
    /// calls by the time of day, which every machine shares, so players together see and hear the same birds in the
    /// same trees.
    /// </para>
    /// </summary>
    public static class AmbientLife
    {
        public const float Range = 40f;
        const float CellSize = 16f;

        /// <summary>What a cell holds, worked out once: each one's kind, home spot and a seed for how it moves.</summary>
        struct Resident
        {
            public AmbientKind Kind;
            public uint Seed;
            public float3 Spot;
            /// <summary>Its tree (for birds, owls, woodpeckers, cicadas): the base, and its crown.</summary>
            public float3 TreeBase;
            public float CrownBase, CrownTop, CrownRadius, TrunkRadius;
        }

        static readonly Dictionary<int2, Resident[]> cells = new Dictionary<int2, Resident[]>();
        static readonly List<Ambient> now = new List<Ambient>();
        static int frame = -1;
        /// <summary>Off for the whole run with the launch flag "-noambientlife" (for benches: the same build without it).</summary>
        static readonly bool Off = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-noambientlife") >= 0;
        static readonly Unity.Profiling.ProfilerMarker NearMarker = new Unity.Profiling.ProfilerMarker("AmbientLife.Near");
        static readonly Unity.Profiling.ProfilerMarker SettleMarker = new Unity.Profiling.ProfilerMarker("AmbientLife.Settle");

        /// <summary>Forgets every cell at the start of play (statics outlive a play session in the editor, and the island
        /// may have changed since).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            cells.Clear();
            now.Clear();
            frame = -1;
        }

        /// <summary>The life round the listener this frame (worked out once a frame, however many ask).</summary>
        public static List<Ambient> Near(World world, float3 ear)
        {
            if (frame == UnityEngine.Time.frameCount)
                return now;
            frame = UnityEngine.Time.frameCount;
            now.Clear();
            if (world == null || Off)
                return now;
            using var marker = NearMarker.Auto();
            var em = world.EntityManager;
            using var tq = em.CreateEntityQuery(typeof(TerrainSource));
            using var fq = em.CreateEntityQuery(typeof(FloraLookup));
            if (tq.IsEmpty || fq.IsEmpty)
                return now;
            var terrain = tq.GetSingleton<TerrainSource>();
            var flora = fq.GetSingleton<FloraLookup>();
            if (!flora.Blob.IsCreated || CaveSystem.Underground > 0.5f)
                return now;
            var cycle = Object.FindAnyObjectByType<Punchy.Weather.DayNightCycle>();
            var ambience = Object.FindAnyObjectByType<Punchy.Ambience.AmbienceSystem>();
            var night = cycle != null ? cycle.Night : 0f;
            var heat = ambience != null ? ambience.Heat : 0f;
            // The shared clock: the time of day in real seconds (a paused clock, for screenshots, runs on local time).
            var t = cycle == null || cycle.Paused ? UnityEngine.Time.time : cycle.TimeOfDay * cycle.DayLengthMinutes * 2.5f;
            var player = ear;
            using (var pq = em.CreateEntityQuery(typeof(Punchy.Player.PlayerTag), typeof(Unity.Transforms.LocalTransform)))
                if (!pq.IsEmpty)
                    player = pq.GetSingleton<Unity.Transforms.LocalTransform>().Position;

            var min = (int2)math.floor((ear.xz - Range) / CellSize);
            var max = (int2)math.floor((ear.xz + Range) / CellSize);
            for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
            {
                var cell = new int2(x, y);
                if (!cells.TryGetValue(cell, out var residents))
                    cells[cell] = residents = Settle(cell, terrain, ref flora.Blob.Value);
                foreach (var r in residents)
                {
                    var d = math.distance(r.Spot.xz, ear.xz);
                    if (d > Range || !Active(r.Kind, night, heat) || (Small(r.Kind) && d > 22f))
                        continue;
                    now.Add(Move(r, t, player, terrain));
                }
            }
            // (Cells long out of range forgotten.)
            if (cells.Count > 400)
            {
                var far = new List<int2>();
                foreach (var c in cells.Keys)
                    if (math.any(math.abs(c - (int2)math.floor(ear.xz / CellSize)) > 8))
                        far.Add(c);
                foreach (var c in far)
                    cells.Remove(c);
            }
            return now;
        }

        static bool Small(AmbientKind k) => k == AmbientKind.Cricket || k == AmbientKind.Grasshopper;

        static bool Active(AmbientKind k, float night, float heat) => k switch
        {
            AmbientKind.Songbird or AmbientKind.Woodpecker => night < 0.4f,
            AmbientKind.Grasshopper => night < 0.4f,
            AmbientKind.Cicada => night < 0.3f && heat > 0.35f,
            AmbientKind.Owl or AmbientKind.Frog => night > 0.6f,
            AmbientKind.Cricket => night > 0.5f,
            _ => false
        };

        /// <summary>Who lives in a cell: from the seed alone, so the same on every machine.</summary>
        static Resident[] Settle(int2 cell, in TerrainSource terrain, ref FloraBlob flora)
        {
            using var marker = SettleMarker.Auto();
            var list = new List<Resident>();
            var seed = math.hash(new int3(cell, 0x5EED)) | 1u;
            var rng = new Unity.Mathematics.Random(seed);
            var centre = ((float2)cell + 0.5f) * CellSize;
            // A tree in the cell: songbirds, and now and then a woodpecker, an owl, cicadas.
            var treeRng = new Unity.Mathematics.Random(seed ^ 0x7EE);
            if (FloraPlacement.FindTree(ref flora, terrain, centre, CellSize * 0.5f, 3f, ref treeRng, out var tree))
            {
                var r = new Resident
                {
                    TreeBase = tree.Base, CrownBase = tree.CrownBase, CrownTop = tree.Height, CrownRadius = math.max(tree.CrownRadius, 0.6f),
                    TrunkRadius = tree.TrunkRadius, Spot = tree.Base
                };
                var birds = rng.NextFloat() < 0.75f ? 1 + rng.NextInt(3) : 0;
                for (int i = 0; i < birds; i++)
                {
                    r.Kind = AmbientKind.Songbird;
                    r.Seed = rng.NextUInt() | 1u;
                    list.Add(r);
                }
                if (rng.NextFloat() < 0.15f) { r.Kind = AmbientKind.Woodpecker; r.Seed = rng.NextUInt() | 1u; list.Add(r); }
                if (rng.NextFloat() < 0.3f) { r.Kind = AmbientKind.Owl; r.Seed = rng.NextUInt() | 1u; list.Add(r); }
                if (rng.NextFloat() < 0.5f) { r.Kind = AmbientKind.Cicada; r.Seed = rng.NextUInt() | 1u; list.Add(r); }
            }
            // The ground: crickets and grasshoppers in grass; frogs where land meets water.
            for (int k = 0; k < 8; k++)
            {
                var spot = centre + (rng.NextFloat2() - 0.5f) * CellSize;
                var ground = terrain.SampleGround(spot);
                var wet = ground.HasWater && ground.WaterSurface > ground.Height;
                if (!wet)
                {
                    // At the water's edge: a frog.
                    // (Just above the water line, or with water a step or two away.)
                    var shore = ground.HasWater && ground.WaterSurface > ground.Height - 0.4f;
                    for (int a = 0; a < 4 && !shore; a++)
                    {
                        math.sincos(a * math.PI * 0.5f, out var sn, out var cs);
                        var g = terrain.SampleGround(spot + new float2(cs, sn) * 2.5f);
                        shore = g.HasWater && g.WaterSurface > g.Height;
                    }
                    if (shore && list.FindAll(x => x.Kind == AmbientKind.Frog).Count < 3)
                    {
                        list.Add(new Resident { Kind = AmbientKind.Frog, Seed = rng.NextUInt() | 1u, Spot = new float3(spot.x, ground.Height, spot.y) });
                        continue;
                    }
                    var surface = terrain.SampleSurface(spot, ground, terrain.SampleNormal(spot, 1f));
                    if (surface.Grass > 0.35f && surface.Snow < 0.3f)
                        list.Add(new Resident { Kind = k % 2 == 0 ? AmbientKind.Cricket : AmbientKind.Grasshopper, Seed = rng.NextUInt() | 1u,
                                                Spot = new float3(spot.x, ground.Height, spot.y) });
                }
            }
            return list.ToArray();
        }

        /// <summary>Where one is this moment, by the shared clock t (seconds).</summary>
        static Ambient Move(in Resident r, float t, float3 player, in TerrainSource terrain)
        {
            var rng = new Unity.Mathematics.Random(r.Seed);
            var a = new Ambient { Kind = r.Kind, Id = r.Seed, Tree = r.TreeBase };
            var phase = rng.NextFloat(0f, 100f);
            switch (r.Kind)
            {
                case AmbientKind.Songbird:
                {
                    // From perch to perch in the crown every few seconds, a quick hop between; now and then a flit down
                    // to the ground under the tree and back. Calls every few seconds.
                    var period = rng.NextFloat(2.5f, 6f);
                    var step = (int)math.floor((t + phase) / period);
                    var u = math.frac((t + phase) / period);
                    var from = Perch(r, r.Seed, step - 1, terrain);
                    var to = Perch(r, r.Seed, step, terrain);
                    var hop = math.saturate(u / 0.12f);
                    a.Position = math.lerp(from, to, math.smoothstep(0f, 1f, hop)) + new float3(0f, math.sin(hop * math.PI) * 0.3f, 0f);
                    a.Air = hop < 1f ? 1f : 0f;
                    var dir = to - from;
                    a.Yaw = math.lengthsq(dir.xz) > 1e-4f ? math.atan2(dir.x, dir.z) : rng.NextFloat(6.28f);
                    var tints = new[] { new float4(0.4f, 0.28f, 0.17f, 0f), new float4(0.7f, 0.58f, 0.12f, 0f), new float4(0.55f, 0.18f, 0.12f, 0f),
                                        new float4(0.25f, 0.35f, 0.55f, 0f) };
                    a.Colour = tints[r.Seed % 4u];
                    a.Calls = (int)math.floor((t + phase * 1.7f) / rng.NextFloat(4f, 11f));
                    break;
                }
                case AmbientKind.Woodpecker:
                {
                    // On the trunk, its side turned to us; drumming every so often.
                    var round = rng.NextFloat(6.28f);
                    var dir = new float3(math.sin(round), 0f, math.cos(round));
                    a.Position = r.TreeBase + dir * (r.TrunkRadius + 0.05f) + new float3(0f, rng.NextFloat(1.8f, math.max(r.CrownBase, 2.5f)), 0f);
                    a.Yaw = round + math.PI;
                    a.Colour = new float4(0.08f, 0.08f, 0.09f, 0f);
                    a.Calls = (int)math.floor((t + phase) / rng.NextFloat(8f, 20f));
                    break;
                }
                case AmbientKind.Owl:
                {
                    // On a low bough of the crown; its head turns to watch whoever's within 25 m; hoots now and then.
                    var round = rng.NextFloat(6.28f);
                    var dir = new float3(math.sin(round), 0f, math.cos(round));
                    a.Position = r.TreeBase + dir * (r.TrunkRadius + r.CrownRadius * 0.35f) + new float3(0f, math.max(r.CrownBase, 2f) + 0.1f, 0f);
                    a.Yaw = round;
                    var to = player - a.Position;
                    if (math.length(to.xz) < 25f)
                        a.Head = math.clamp(math.atan2(math.sin(math.atan2(to.x, to.z) - a.Yaw), math.cos(math.atan2(to.x, to.z) - a.Yaw)), -2.3f, 2.3f);
                    else
                        a.Head = math.sin(t * 0.3f + phase) * 1.2f;
                    a.Colour = new float4(0.32f, 0.24f, 0.16f, 0f);
                    a.Calls = (int)math.floor((t + phase) / rng.NextFloat(10f, 25f));
                    break;
                }
                case AmbientKind.Cicada:
                {
                    var round = rng.NextFloat(6.28f);
                    var dir = new float3(math.sin(round), 0f, math.cos(round));
                    a.Position = r.TreeBase + dir * (r.TrunkRadius + 0.02f) + new float3(0f, rng.NextFloat(1.2f, 2.5f), 0f);
                    a.Yaw = round + math.PI;
                    a.Colour = new float4(0.16f, 0.22f, 0.1f, 0f);
                    a.Calls = (int)math.floor((t + phase) / rng.NextFloat(7f, 16f));
                    break;
                }
                case AmbientKind.Frog:
                {
                    // Sitting at the edge, facing the water; croaking; into the water (gone) when someone comes within 4 m.
                    a.Position = r.Spot + new float3(0f, 0.02f, 0f);
                    a.Yaw = rng.NextFloat(6.28f);
                    a.Colour = new float4(0.15f, 0.3f, 0.1f, 0f);
                    a.Hidden = math.distance(player.xz, r.Spot.xz) < 4f;
                    a.Calls = (int)math.floor((t + phase) / rng.NextFloat(3f, 8f));
                    break;
                }
                default:
                {
                    // Crickets and grasshoppers: in the grass, hopping now and then (a grasshopper often, a cricket
                    // seldom), away from anyone who comes within 2 m; a cricket chirps steadily.
                    var cricket = r.Kind == AmbientKind.Cricket;
                    var period = cricket ? rng.NextFloat(6f, 14f) : rng.NextFloat(2.5f, 6f);
                    var step = (int)math.floor((t + phase) / period);
                    var u = math.frac((t + phase) / period);
                    var srng = new Unity.Mathematics.Random(r.Seed ^ (uint)(step * 9781 + 1));
                    var spot = r.Spot + new float3(srng.NextFloat(-1.5f, 1.5f), 0f, srng.NextFloat(-1.5f, 1.5f));
                    var away = spot.xz - player.xz;
                    if (math.length(away) < 2f)
                        spot.xz = player.xz + math.normalizesafe(away, new float2(1f, 0f)) * 2.5f;
                    spot.y = terrain.SampleHeight(spot.xz) + 0.03f;
                    var hop = math.saturate(u / 0.15f);
                    a.Position = spot + new float3(0f, math.sin(hop * math.PI) * (cricket ? 0.15f : 0.3f), 0f);
                    a.Air = hop < 1f ? 1f : 0f;
                    a.Yaw = srng.NextFloat(6.28f);
                    a.Colour = cricket ? new float4(0.12f, 0.09f, 0.06f, 0f) : new float4(0.35f, 0.42f, 0.15f, 0f);
                    // (A cricket's chirps come steadily; a grasshopper buzzes as it jumps.)
                    a.Calls = cricket ? (int)math.floor((t + phase) / rng.NextFloat(1.5f, 4f)) : step;
                    break;
                }
            }
            return a;
        }

        /// <summary>A bird's perch number n in its tree's crown (or, one time in seven, the ground under it).</summary>
        static float3 Perch(in Resident r, uint seed, int n, in TerrainSource terrain)
        {
            var rng = new Unity.Mathematics.Random(math.hash(new uint2(seed, (uint)n)) | 1u);
            var round = rng.NextFloat(6.28f);
            var dir = new float3(math.sin(round), 0f, math.cos(round));
            if (rng.NextFloat() < 0.14f)
            {
                var g = r.TreeBase.xz + dir.xz * (r.CrownRadius + rng.NextFloat(0.5f, 2f));
                return new float3(g.x, terrain.SampleHeight(g), g.y);
            }
            // (Where a bird can be seen, not buried in the leaves: on top of the crown, against the sky, or out at its
            // lower edge.)
            if (rng.NextFloat() < 0.5f)
                return r.TreeBase + dir * (r.TrunkRadius + r.CrownRadius * rng.NextFloat(0.3f, 0.6f)) + new float3(0f, r.CrownTop + 0.05f, 0f);
            var height = math.lerp(r.CrownBase, r.CrownTop, rng.NextFloat(0f, 0.2f));
            return r.TreeBase + dir * (r.TrunkRadius + r.CrownRadius * rng.NextFloat(1.0f, 1.15f)) + new float3(0f, height, 0f);
        }
    }
}
