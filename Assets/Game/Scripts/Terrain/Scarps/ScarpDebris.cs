using Punchy.Flora;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// The rubble round a scarp (W-155 row 2): scree stones in the fans at the foot (bigger ones rolling further out,
    /// as on a real slope), blocks fallen from the beds above lying past the foot, blocks loosened along the rim's
    /// bare strip, and small stones on ledge tops. All placed from the scarp record and its seed (rule 22), sat on the
    /// ground from <see cref="TerrainSource"/>, never on a flora rock, and coloured from the bed they fell from with
    /// the face's own tones (<see cref="ScarpPaint.Bed"/>), so a block reads as a piece of the wall above it.
    /// </summary>
    public struct ScarpBlock
    {
        public float3 Position, Half;
        public quaternion Turn;
        public float3 Tone;
        /// <summary>How rounded (0 a sharp slab, 1 a boulder), how much moss on its top, its noise offset.</summary>
        public float Round, Moss, Seed;
        /// <summary>Which draw group it goes in (<see cref="ScarpDebris.Group"/>).</summary>
        public int Group;
    }

    public static class ScarpDebris
    {
        /// <summary>Rim length per draw group (m), so each stretch is shown by its own on-screen size.</summary>
        public const float GroupLength = 40f;
        /// <summary>Grid cells per face of a block (a cube rounded towards a ball) and what that makes.</summary>
        public const int Cells = 3, PerFace = (Cells + 1) * (Cells + 1);
        public const int Vertices = 6 * PerFace, Indices = 6 * Cells * Cells * 6;

        /// <summary>Draw groups per scarp: big pieces (fallen and rim blocks) and small (scree, ledge stones) per
        /// stretch of rim, so the small ones leave the screen sooner.</summary>
        public static int Groups(ref Scarp sc) => 2 * ((int)math.ceil(sc.Length / GroupLength) + 1);

        public static int Group(ref Scarp sc, float s, bool small) => 2 * (int)math.clamp(s / GroupLength, 0f, math.ceil(sc.Length / GroupLength)) + (small ? 1 : 0);

        /// <summary>W-162 row 4: draw groups per cave mouth (its big and its small stones), after every scarp's.</summary>
        public const int MouthGroups = 2;

        /// <summary>All the cave mouths' draw groups (one pair per cave, tunnels included so the index is the cave's).</summary>
        public static int MouthGroupTotal(in TerrainSource source) =>
            source.Caves.IsCreated ? MouthGroups * source.Caves.Value.Caves.Length : 0;
    }

    /// <summary>
    /// Places every scarp's rubble (<see cref="ScarpBlock"/>), walking each rim half a metre at a time. One job for
    /// the whole set; the system groups the blocks and builds their meshes with <see cref="ScarpDebrisMeshJob"/>.
    /// </summary>
    [BurstCompile]
    struct ScarpDebrisPlaceJob : IJob
    {
        const float Step = 0.5f, ToneOut = 2.5f;

        public TerrainSource Source;
        public BlobAssetReference<FloraBlob> Flora;
        public bool HasFlora;
        public NativeList<ScarpBlock> Blocks;
        /// <summary>Offset of each scarp's first group in the whole set's groups.</summary>
        public NativeArray<int> GroupStart;

        public void Execute()
        {
            ref var set = ref Source.Scarps.Value;
            int groups = 0;
            for (int i = 0; i < set.Scarps.Length; i++)
            {
                GroupStart[i] = groups;
                ref var sc = ref set.Scarps[i];
                Place(ref sc, groups);
                groups += ScarpDebris.Groups(ref sc);
            }
            if (!Source.Caves.IsCreated)
                return;
            ref var caves = ref Source.Caves.Value;
            for (int i = 0; i < caves.Caves.Length; i++, groups += ScarpDebris.MouthGroups)
                if (caves.Caves[i].Tunnel == 0 && caves.Caves[i].NodeCount > 1)
                    Mouth(ref caves, i, groups);
        }

        /// <summary>
        /// W-162 row 4 (#327): a cave mouth gets the cliff toe's fallen stone: rubble lying round the rim, thickest
        /// where the mouth opens downhill (fallen from its brow and rolled out), now and then a bigger block, in the
        /// country rock's colour the walls take at the mouth (the ground's own steep rock) with a little of the earth
        /// round it. Rays walk out from the mouth to its rim (where the terrain's mouth cut ends) and every stone lies
        /// on the ground past it, never over the opening. Rolled from the cave's own stream (its Key), so no scarp's
        /// rubble moves.
        /// </summary>
        void Mouth(ref CaveSetBlob set, int index, int group)
        {
            const int Rays = 64;
            const float Reach = 16f, March = 0.25f, GridStep = 1f;
            var cave = set.Caves[index];
            var rng = new Unity.Mathematics.Random(math.hash(new uint2(cave.Key, 0x6d6f7574u)) | 1u);
            var outward = math.normalizesafe((cave.Mouth - set.Nodes[cave.FirstNode + 1].Position).xz, new float2(1f, 0f));
            var flat = new float3(0f, 1f, 0f);
            for (int r = 0; r < Rays; r++)
            {
                // Every roll is drawn whatever happens, so one ray's choices never shift the next's.
                float4 r0 = rng.NextFloat4(), r1 = rng.NextFloat4(), r2 = rng.NextFloat4();
                float4 q0 = rng.NextFloat4(), q1 = rng.NextFloat4(), q2 = rng.NextFloat4(), q3 = rng.NextFloat4();
                float angle = (r + r0.x) * (2f * math.PI / Rays);
                var dir = new float2(math.cos(angle), math.sin(angle));
                float rim = -1f;
                for (float t = 0.5f; t <= Reach; t += March)
                    if (BuildChunkMeshJob.MouthCutAt(Source, cave.Mouth.xz + dir * t, GridStep) > 0f)
                    {
                        rim = t;
                        break;
                    }
                if (rim < 0f)
                    continue;
                // More stone on the open, downhill side; a little all round the rim.
                float spill = math.saturate(0.3f + 0.7f * math.dot(dir, outward));
                var edge = cave.Mouth.xz + dir * rim;
                var ground = Source.SampleGround(edge);
                var normal = Source.SampleNormal(edge, 1f);
                var down = math.normalizesafe(normal.xz, dir);
                var steep = new float3(down.x * 0.94f, 0.342f, down.y * 0.94f);
                var rock = Source.SampleSurface(edge, ground, steep).Albedo;
                var earth = Source.SampleSurface(edge, ground, flat).Albedo;
                var tone = math.lerp(rock, math.dot(earth, new float3(0.3f, 0.59f, 0.11f)) * new float3(1.08f, 1f, 0.88f), 0.25f);
                var side = new float2(-dir.y, dir.x);
                float4x4 stones = new float4x4(q0, q1, q2, q3);
                for (int m = 0; m < 3; m++)
                {
                    float4 q = stones[m], p = stones[m + 1].wzyx;
                    if (q.x >= 0.6f * spill + 0.1f)
                        continue;
                    float f = math.pow(q.y, 1.6f);
                    float h = math.lerp(0.07f, 0.24f, q.z) * (1f + 1.2f * f);
                    var xz = edge + dir * (0.4f + f * (1.5f + 4f * spill)) + side * (q.w - 0.5f) * rim * 0.1f;
                    Lay(xz, new float3(h * (1f + 0.4f * p.x), h * (0.6f + 0.3f * p.y), h), p.z, p.wx,
                        tone * (0.85f + 0.25f * p.y), 0.6f, 0.2f, 0.3f, group + 1);
                }
                // Now and then a block off the brow, lying a little out from the rim.
                if (r1.x < 0.14f * spill)
                {
                    float h = math.lerp(0.35f, 0.9f, r1.y * r1.y);
                    var xz = edge + dir * (0.8f + h + r1.z * 2.5f * spill);
                    var half = new float3(h * (1.1f + 0.5f * r1.w), h * (0.5f + 0.3f * r2.x), h * (0.8f + 0.3f * r2.y));
                    Lay(xz, half, r2.z, r2.wx * 0.6f, tone * (0.9f + 0.2f * r0.y), 0.35f, 0.4f, 0.35f, group);
                }
            }
        }

        /// <summary>Lays one mouth stone (<see cref="Put"/>) where it sits wholly on the ground past the rim.</summary>
        void Lay(float2 xz, float3 half, float yaw, float2 tilt, float3 tone, float round, float moss, float sink, int group)
        {
            if (BuildChunkMeshJob.MouthCutAt(Source, xz, 1f) < 0.3f + math.cmax(half))
                return;
            Put(xz, half, yaw, tilt, tone, round, moss, sink, group);
        }

        void Place(ref Scarp sc, int group0)
        {
            var rng = new Unity.Mathematics.Random((sc.Seed * 2654435761u + 977u) | 1u);
            bool earth = sc.Kind == ScarpKind.RiverCut;
            for (int seg = 0; seg < sc.Nodes.Length - 1; seg++)
            {
                float len = math.distance(sc.Nodes[seg].P, sc.Nodes[seg + 1].P);
                int n = math.max(1, (int)math.round(len / Step));
                for (int k = 0; k < n; k++)
                {
                    ScarpSet.Column(ref sc, seg, (k + 0.5f) / n, out var at, out var rim, out var dir);
                    float drop = at.RimY - at.FootY;
                    // Every roll is drawn whatever happens, so one choice never shifts the next.
                    float4 r0 = rng.NextFloat4(), r1 = rng.NextFloat4(), r2 = rng.NextFloat4(), r3 = rng.NextFloat4();
                    EndOutcrop(ref sc, seg, k, at, rim, dir, group0);
                    if (drop < 1f || at.W < 1f || at.S < 3f || at.S > sc.Length - 3f)
                        continue;
                    var along = new float2(-dir.y, dir.x);
                    var cracks = ScarpSet.Cracks(ref sc, at.S);
                    float fan = math.saturate(2f * ScarpLife.Fan(ref sc, at.S) + 0.4f);
                    float size = math.clamp(drop / 30f, 0.35f, 1.5f);

                    // Scree: in the fans, a few stones per column, thickest near the wall; the bigger stones roll
                    // furthest. The tone of the bed each one came from is read once per column.
                    var screeTones = Tones(ref sc, at, cracks, at.FootY + r1.x * drop, rim, dir);
                    for (int m = 0; m < 3; m++)
                    {
                        float4 q = rng.NextFloat4(), p = rng.NextFloat4();
                        if (q.x >= 0.5f * fan * (earth ? 0.4f : 1f))
                            continue;
                        float f = math.pow(q.y, 1.6f);
                        float d = at.W - 0.3f + f * (2f + 0.12f * drop);
                        float h = math.lerp(0.08f, 0.28f, q.z) * (1f + 1.3f * f) * math.lerp(0.7f, 1f, size);
                        var xz = rim + dir * d + along * (q.w - 0.5f) * Step;
                        Add(ref sc, xz, new float3(h * (1f + 0.4f * p.x), h * (0.6f + 0.3f * p.y), h), p.z, p.wx,
                            ScarpPaint.Scree(screeTones) * (0.9f + 0.2f * p.y), 0.7f, earth ? 0.4f : 0.15f, 0.3f, at.S, true, group0);
                    }
                    // Fallen blocks: a slab of a bed from somewhere up the face, lying past the foot.
                    if (r2.z < Step / (earth ? 18f : 11f))
                    {
                        float d = at.W + 0.5f + r2.w * (0.1f + 0.3f * r3.x) * drop;
                        float h = math.lerp(0.5f, 1.5f, r3.y * r3.y) * size;
                        var xz = rim + dir * d;
                        var tones = Tones(ref sc, at, cracks, at.FootY + (0.2f + 0.8f * r3.z) * drop, rim, dir);
                        var half = new float3(h * (1.1f + 0.6f * r3.w), h * (0.45f + 0.3f * r0.x), h * (0.8f + 0.3f * r1.x));
                        Add(ref sc, xz, half, r1.y, r1.zw * 0.6f, tones.Tone, 0.25f + 0.5f * sc.Age, earth ? 0.7f : 0.45f, 0.35f, at.S, false, group0);
                    }
                    // Rim blocks: loosened along the bare strip behind the rim, half sunk in the thin soil.
                    if (r3.x > 1f - Step / 8f)
                    {
                        float d = -(0.3f + r3.y * (ScarpLife.Bare(ref sc, at.S) - 0.3f));
                        float h = math.lerp(0.25f, 0.7f, r3.z) * math.lerp(0.6f, 1f, size);
                        var xz = rim + dir * d;
                        var tones = Tones(ref sc, at, cracks, at.RimY - 1f, rim, dir);
                        Add(ref sc, xz, new float3(h * 1.3f, h * 0.6f, h), r3.w, r2.xy * 0.4f, tones.Tone, 0.3f + 0.4f * sc.Age, 0.5f, 0.45f, at.S, false, group0);
                    }
                    // Ledge stones: only where the ground on the face is a ledge top (flat rock out on the strip).
                    if (r2.x < 0.1f)
                    {
                        float d = 0.3f + r2.y * (at.W - 0.8f);
                        var xz = rim + dir * d;
                        float y = Source.SampleHeight(xz);
                        if (y > at.FootY + 1f && y < at.RimY - 0.5f && Source.SampleNormal(xz, 0.25f).y > 0.85f)
                        {
                            float h = math.lerp(0.08f, 0.25f, r0.w);
                            var tones = Tones(ref sc, at, cracks, y + 0.5f, rim, dir);
                            Add(ref sc, xz, new float3(h * 1.2f, h * 0.7f, h), r1.w, r2.zw * 0.5f, tones.Tone, 0.5f, 0.3f, 0.25f, at.S, true, group0);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Row 2b: where a strip tapers out at an end, the wall does not just stop: it breaks up into outcrops and
        /// boulders of its rock, standing out of the slope along where the face would run and sunk deeper the lower
        /// the face gets, until only their backs show. Rolled from their own stream per column, so they never shift
        /// the rest of the debris.
        /// </summary>
        void EndOutcrop(ref Scarp sc, int seg, int k, in ScarpAt at, float2 rim, float2 dir, int group0)
        {
            float start = ScarpSet.ClipSteps;
            if (at.W < 0.3f || at.W > start + 8f || at.W > 0.7f * sc.Width)
                return;
            var rng = new Unity.Mathematics.Random(math.hash(new uint3(sc.Seed, (uint)seg, (uint)k)) | 1u);
            float4 q = rng.NextFloat4(), p = rng.NextFloat4();
            float end = 1f - math.smoothstep(start, start + 8f, at.W);   // 1 where the skin stops, 0 a band in
            if (q.x >= Step * (0.2f + 0.4f * end))
                return;
            bool earth = sc.Kind == ScarpKind.RiverCut;
            float drop = math.max(at.RimY - at.FootY, 0.3f);
            var along = new float2(-dir.y, dir.x);
            // Across the strip, mostly in its lower half (where the wall's foot would be), a little along the rim.
            var xz = rim + dir * (at.W * (0.2f + 0.8f * q.y)) + along * (q.z - 0.5f) * 1.5f;
            float h = math.clamp(drop * (0.35f + 0.4f * p.x), 0.25f, earth ? 0.8f : 2.2f);
            var half = new float3(h * (0.9f + 0.6f * p.y), h * (0.6f + 0.3f * p.z), h * (0.8f + 0.4f * p.w));
            var cracks = ScarpSet.Cracks(ref sc, at.S);
            var tones = Tones(ref sc, at, cracks, at.FootY + (0.3f + 0.6f * q.w) * drop, rim, dir);
            Add(ref sc, xz, half, q.w, p.xy * 0.5f, tones.Tone, 0.35f + 0.4f * sc.Age, earth ? 0.6f : 0.4f,
                0.35f + 0.3f * end, at.S, false, group0);
        }

        ScarpPaint.BedTone Tones(ref Scarp sc, in ScarpAt at, float3 cracks, float y, float2 rim, float2 dir)
        {
            var flat = new float3(0f, 1f, 0f);
            var top = rim - dir * ToneOut;
            var foot = rim + dir * (at.W + ToneOut);
            var rimTone = Source.SampleSurface(top, Source.SampleGround(top), flat).Albedo;
            var footTone = Source.SampleSurface(foot, Source.SampleGround(foot), flat).Albedo;
            return ScarpPaint.Bed(ref sc, at, cracks, y, rimTone, footTone);
        }

        /// <summary>Sits one block on the ground (on the lowest of five points under it, sunk by its share of its
        /// height), unless it would stand on a flora rock or drown.</summary>
        void Add(ref Scarp sc, float2 xz, float3 half, float yaw, float2 tilt, float3 tone, float round, float moss,
                 float sink, float s, bool small, int group0) =>
            Put(xz, half, yaw, tilt, tone, round, moss, sink, group0 + ScarpDebris.Group(ref sc, s, small));

        /// <summary>Sits one block on the ground in draw group group (Add's work, shared with the cave mouths').</summary>
        void Put(float2 xz, float3 half, float yaw, float2 tilt, float3 tone, float round, float moss, float sink, int group)
        {
            var turn = math.mul(quaternion.RotateY(yaw * 2f * math.PI), quaternion.Euler(tilt.x - 0.5f * tilt.y, 0f, tilt.y - 0.5f * tilt.x));
            float reach = math.max(half.x, half.z);
            float y = Source.SampleHeight(xz);
            y = math.min(y, Source.SampleHeight(xz + new float2(reach, 0f)));
            y = math.min(y, Source.SampleHeight(xz - new float2(reach, 0f)));
            y = math.min(y, Source.SampleHeight(xz + new float2(0f, reach)));
            y = math.min(y, Source.SampleHeight(xz - new float2(0f, reach)));
            var ground = Source.SampleGround(xz);
            if (ground.HasWater && ground.WaterSurface - y > 1.2f * half.y)
                return;
            if (HasFlora && FloraPlacement.RockAt(ref Flora.Value, Source, xz, reach, out _))
                return;
            Blocks.Add(new ScarpBlock
            {
                Position = new float3(xz.x, y + half.y * (1f - 2f * sink), xz.y), Half = half, Turn = turn, Tone = tone,
                Round = round, Moss = moss, Seed = (Blocks.Length * 7.31f) % 113f,
                Group = group
            });
        }
    }

    /// <summary>
    /// Builds one draw group's mesh of rubble blocks, in the terrain's vertex layout so the terrain's own material
    /// draws them (the same rock, light and haze as the face). Each block is a cube rounded part way to a ball,
    /// stretched to its size, roughened by noise, turned and set down; its faces keep their own normals at the
    /// cube's edges, so a block reads as broken rock.
    /// </summary>
    [BurstCompile]
    struct ScarpDebrisMeshJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] public Mesh.MeshDataArray Meshes;
        [ReadOnly] public NativeArray<ScarpBlock> Blocks;
        /// <summary>Per mesh: the first entry in Order and how many blocks.</summary>
        [ReadOnly] public NativeArray<int2> Ranges;
        [ReadOnly] public NativeArray<int> Order;
        public TerrainSource Source;
        public bool Clay;

        public void Execute(int index)
        {
            var mesh = Meshes[index];
            var verts = mesh.GetVertexData<TerrainVertex>();
            var indices = mesh.GetIndexData<uint>();
            var range = Ranges[index];
            for (int b = 0; b < range.y; b++)
                Block(Blocks[Order[range.x + b]], b, verts, indices);
        }

        void Block(in ScarpBlock block, int b, NativeArray<TerrainVertex> verts, NativeArray<uint> indices)
        {
            int v0 = b * ScarpDebris.Vertices, i0 = b * ScarpDebris.Indices;
            var ground = Source.SampleGround(block.Position.xz);
            for (int face = 0; face < 6; face++)
            {
                for (int j = 0; j <= ScarpDebris.Cells; j++)
                for (int i = 0; i <= ScarpDebris.Cells; i++)
                {
                    float u = i / (float)ScarpDebris.Cells * 2f - 1f, v = j / (float)ScarpDebris.Cells * 2f - 1f;
                    const float e = 0.05f;
                    var p = Point(block, face, u, v);
                    var du = Point(block, face, u + e, v) - Point(block, face, u - e, v);
                    var dv = Point(block, face, u, v + e) - Point(block, face, u, v - e);
                    var n = math.normalizesafe(math.cross(du, dv), Axis(face));
                    if (math.dot(n, Axis(face)) < 0f)
                        n = -n;
                    var position = block.Position + math.mul(block.Turn, p);
                    var normal = math.mul(block.Turn, n);
                    var vertex = new TerrainVertex { Position = position, Normal = normal };
                    var surface = Surface(block, ground, position, normal);
                    vertex.SetSurface(surface);
                    vertex.Morph = new float4(1e9f, 2e9f, surface.Bump, 100f);
                    verts[v0 + face * ScarpDebris.PerFace + j * (ScarpDebris.Cells + 1) + i] = vertex;
                }
                for (int j = 0; j < ScarpDebris.Cells; j++)
                for (int i = 0; i < ScarpDebris.Cells; i++)
                {
                    uint a = (uint)(v0 + face * ScarpDebris.PerFace + j * (ScarpDebris.Cells + 1) + i);
                    uint c = a + 1, d = a + (uint)(ScarpDebris.Cells + 1), f = d + 1;
                    int w = i0 + (face * ScarpDebris.Cells * ScarpDebris.Cells + j * ScarpDebris.Cells + i) * 6;
                    // Clockwise seen from outside (Unity's front face).
                    indices[w] = a; indices[w + 1] = c; indices[w + 2] = d;
                    indices[w + 3] = c; indices[w + 4] = f; indices[w + 5] = d;
                }
            }
        }

        SurfaceProps Surface(in ScarpBlock block, in GroundSample ground, float3 position, float3 normal)
        {
            if (Clay)
                return ScarpClay.Surface();
            var g = ground;
            g.Height = position.y;
            var surface = Source.SampleSurface(position.xz, g, normal);
            // The bed's tone over the terrain's own rock, darker underneath where grime gathers, moss and a little
            // grass on top on the damp ones.
            var albedo = math.lerp(surface.Albedo, block.Tone, 0.8f);
            albedo *= 1f - 0.3f * math.smoothstep(0f, -0.7f, normal.y);
            float top = math.smoothstep(0.55f, 0.9f, normal.y);
            float patch = math.saturate(0.5f + 1.5f * noise.snoise(position.xz * 0.9f + block.Seed));
            float moss = block.Moss * top * patch;
            albedo = math.lerp(albedo, new float3(0.07f, 0.1f, 0.035f), 0.5f * moss);
            surface.Albedo = albedo;
            surface.Grass = 0.1f * moss;
            surface.Strata = 0f;
            surface.Paving = 0f;
            surface.Pebbles = 0f;
            return surface;
        }

        static float3 Axis(int face) => face switch
        {
            0 => new float3(1f, 0f, 0f), 1 => new float3(-1f, 0f, 0f), 2 => new float3(0f, 1f, 0f),
            3 => new float3(0f, -1f, 0f), 4 => new float3(0f, 0f, 1f), _ => new float3(0f, 0f, -1f)
        };

        /// <summary>A point of the block in its own frame: a point on one face of the cube, rounded towards the
        /// ball by the block's roundness, stretched to its size and roughened. Winding is outward for every face.</summary>
        static float3 Point(in ScarpBlock block, int face, float u, float v)
        {
            var n = Axis(face);
            // Two axes across the face whose cross product is the face's outward axis.
            var t = math.abs(n.y) > 0.5f ? new float3(0f, 0f, 1f) : new float3(0f, 1f, 0f);
            var a = math.cross(t, n);
            var bb = math.cross(n, a);
            var p = n + a * u + bb * v;
            var ball = math.normalize(p) * 1.2f;
            var shape = math.lerp(p, ball, block.Round);
            float rough = 1f + 0.1f * noise.snoise(ball * 1.3f + block.Seed) + 0.04f * noise.snoise(ball * 3.7f + block.Seed + 17f);
            return shape * block.Half * rough;
        }
    }
}
