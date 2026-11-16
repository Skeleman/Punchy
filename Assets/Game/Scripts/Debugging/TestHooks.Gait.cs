#if UNITY_EDITOR
using System.Collections.Generic;
using Punchy.Fauna;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Debugging
{
    /// <summary>Test hooks for per-person gait (A-124 row 2): how far planted feet slide for each way of walking.</summary>
    public static partial class TestHooks
    {
        /// <summary>
        /// Every upright species walked and run in a straight line at x, z (size 1, heading +z) for each GaitKind, posed by
        /// CreatureAnimator.Evaluate with the phase advanced by CreatureMotion.AdvancePhase as in play. One line per species
        /// and kind: "name kind walkSlip runSlip stance", where a slip is how far the root legs' feet move over the ground
        /// while planted (the middle of their stance, cm per metre walked) and stance the sore leg's share of the time on
        /// the ground over the good one's (1 for an even walk).
        /// </summary>
        public static string GaitSlip(float x, float z)
        {
            var director = Object.FindAnyObjectByType<FaunaDirector>();
            if (director == null || !Source(out var source))
                return "";
            var lines = new System.Text.StringBuilder();
            var seen = new HashSet<string>();
            for (int i = 0; i < director.SpeciesCount; i++)
            {
                var sp = director.SpeciesAt(i);
                if (sp == null || !sp.Upright || !seen.Add(sp.name))
                    continue;
                var blob = CreatureBlobBuilder.Build(sp);
                ref var b = ref blob.Value;
                var output = new NativeArray<SegmentInstance>(b.Segments.Length, Allocator.Temp);
                for (var kind = GaitKind.Even; kind <= GaitKind.Limp; kind++)
                {
                    var individual = IndividualWith(kind, b.Child);
                    if (individual == 0u)
                        continue;
                    var gait = GaitStyle.Of(ref b, individual);
                    var walk = Slip(ref b, in source, output, x, z, individual, b.WalkSpeed * gait.Pace, out var stance);
                    var run = Slip(ref b, in source, output, x, z, individual, b.RunSpeed * (1f + (gait.Pace - 1f) * 0.5f), out _);
                    lines.Append(sp.name).Append(' ').Append(kind).Append(' ').Append(walk.ToString("0.00")).Append(' ')
                         .Append(run.ToString("0.00")).Append(' ').Append(stance.ToString("0.000")).Append('\n');
                }
                output.Dispose();
                blob.Dispose();
            }
            return lines.ToString();
        }

        /// <summary>The first Individual (odd, as the spawner makes them) whose walk is this kind.</summary>
        static uint IndividualWith(GaitKind kind, bool child)
        {
            for (uint n = 1; n < 4000; n++)
            {
                var id = Punchy.Terrain.BiomeSampling.Scramble(n * 7919u) | 1u;
                if (GaitStyle.Of(id, true, child).Kind == kind)
                    return id;
            }
            return 0u;
        }

        /// <summary>One walk for GaitSlip: the planted feet's slide (cm per metre) and the sore leg's stance share.</summary>
        static float Slip(ref CreatureBlob b, in Punchy.Terrain.TerrainSource source, NativeArray<SegmentInstance> output,
                          float x, float z, uint individual, float speed, out float stance)
        {
            const float dt = 1f / 60f;
            var gait = GaitStyle.Of(ref b, individual);
            var s = new CreatureState
            {
                Position = new float3(x, source.SampleHeight(new float2(x, z)), z),
                Yaw = 0f, Scale = 1f, Mode = CreatureMode.Puppet, Speed = speed, GaitSpeed = speed,
                Clock = 1f, Seed = individual, Individual = individual
            };
            var last = new float3[b.Parts.Length];
            var was = new bool[b.Parts.Length];
            float slide = 0f, travelled = 0f, sore = 0f, good = 0f;
            for (int f = 0; f < 420; f++)
            {
                CreatureMotion.AdvancePhase(ref s, ref b, dt);
                s.Position.z += speed * dt;
                s.Position.y = source.SampleHeight(s.Position.xz);
                s.Clock += dt;
                CreatureAnimator.Evaluate(ref b, in s, 0, in source, output, 0);
                var counting = f >= 60;
                if (counting)
                    travelled += speed * dt;
                var sprint = b.Sprint(s, s.GaitSpeed);
                for (int p = 0; p < b.Parts.Length; p++)
                {
                    ref var part = ref b.Parts[p];
                    if (part.Role != LimbRole.Leg || part.Parent >= 0 || part.SegmentCount == 0)
                        continue;
                    var g = output[part.FirstSegment + part.SegmentCount - 1];
                    var tip = g.Position + math.mul(g.Rotation, new float3(0f, 0f, g.Length));
                    var phase = math.frac(s.Phase + b.LegPhase(ref part, sprint));
                    var planted = phase > 0.1f && phase < 0.4f;
                    if (counting && planted && was[p])
                        slide += math.distance(tip.xz, last[p].xz);
                    if (counting && phase < 0.5f)
                    {
                        if (gait.Limps && part.Side * gait.LimpSide > 0f)
                            sore += dt;
                        else if (gait.Limps)
                            good += dt;
                    }
                    last[p] = tip;
                    was[p] = planted;
                }
            }
            stance = good > 0f ? sore / good : 1f;
            return travelled > 0f ? slide * 100f / travelled : 0f;
        }
        static readonly List<Unity.Entities.Entity> parade = new List<Unity.Entities.Entity>();

        /// <summary>
        /// A-124 row 2 clips: one person of each GaitKind of a species (the first of each among fresh spawns; the rest
        /// are sent off), set walking (or running) towards +x as puppets at the pace their brain would give them, one
        /// row behind the other from z (1.8 m apart), starting back from x. Returns [count, ground y at x, z]; the kinds
        /// go to GaitTrail. Earlier parades are cleared first.
        /// </summary>
        public static float[] GaitParade(string species, float x, float z, bool run, float back)
        {
            GaitParadeClear();
            GaitTrail = "";
            var director = Object.FindAnyObjectByType<FaunaDirector>();
            if (director == null || !Source(out var source))
                return new float[3];
            var index = director.SpeciesIndex(species);
            var ground = source.SampleHeight(new float2(x, z));
            if (index < 0)
                return new[] { 0f, ground, 0f };
            var picked = new Unity.Entities.Entity[5];
            for (int tries = 0; tries < 150; tries++)
            {
                var e = director.Spawn(index, new float3(x - back, ground, z - 30f), math.PI / 2f);
                var s = EM.GetComponentData<CreatureState>(e);
                var blob = EM.GetComponentData<CreatureBody>(e).Blob;
                ref var b = ref blob.Value;
                var kind = (int)GaitStyle.Of(ref b, s.Individual).Kind;
                if (picked[kind] == Unity.Entities.Entity.Null)
                    picked[kind] = e;
                else
                {
                    s.Gone = true;
                    EM.SetComponentData(e, s);
                }
                var full = true;
                for (int k = 0; k < 5; k++)
                    full &= picked[k] != Unity.Entities.Entity.Null || k == (int)GaitKind.Limp && b.Child;
                if (full)
                    break;
            }
            var row = 0;
            for (int k = 0; k < 5; k++)
            {
                var e = picked[k];
                if (e == Unity.Entities.Entity.Null)
                    continue;
                var s = EM.GetComponentData<CreatureState>(e);
                var blob = EM.GetComponentData<CreatureBody>(e).Blob;
                ref var b = ref blob.Value;
                var gait = GaitStyle.Of(ref b, s.Individual);
                // (As CreatureBrainSystem paces them: the trait's few percent, then the style's.)
                var pace = 1f + CreatureBlob.Trait(s.Individual, 5) * 0.08f;
                var at = new float2(x - back, z + row * 1.8f);
                s.Position = new float3(at.x, source.SampleHeight(at), at.y);
                s.Yaw = math.PI / 2f;
                s.Mode = CreatureMode.Puppet;
                s.Speed = run ? b.RunSpeed * s.Scale * pace * (1f + (gait.Pace - 1f) * 0.5f) : b.WalkSpeed * s.Scale * pace * gait.Pace;
                // (Legs posed for that pace from the start: a puppet's brain caps its speed at its gait's plus a frame's
                // acceleration, and only a steered puppet eases the gait up.)
                s.GaitSpeed = s.Speed;
                EM.SetComponentData(e, s);
                paradeSpeed[e] = s.Speed;
                parade.Add(e);
                GaitTrail += $"{(GaitKind)k}{(gait.Limps ? (gait.LimpSide < 0f ? "(left)" : "(right)") : "")} {s.Speed:0.00} m/s; ";
                row++;
            }
            return new[] { row, ground, 0f };
        }

        /// <summary>The kinds and speeds the last GaitParade set going, front row first.</summary>
        public static string GaitTrail;

        static readonly Dictionary<Unity.Entities.Entity, float2> paradeWas = new Dictionary<Unity.Entities.Entity, float2>();
        static readonly Dictionary<Unity.Entities.Entity, float> paradeSpeed = new Dictionary<Unity.Entities.Entity, float>();

        /// <summary>
        /// Keeps the last GaitParade walking, called once a frame: a puppet the brain didn't move since the last call
        /// (held still this frame) is walked on by dt along its heading at its speed, on the ground; one it moved is left.
        /// Returns how many it walked on.
        /// </summary>
        public static int GaitParadeStep(float dt)
        {
            if (!Source(out var source))
                return 0;
            var walked = 0;
            foreach (var e in parade)
            {
                if (!EM.Exists(e) || !EM.HasComponent<CreatureState>(e))
                    continue;
                var s = EM.GetComponentData<CreatureState>(e);
                // (Kept at its pace: nothing else holds a puppet's speed up.)
                if (paradeSpeed.TryGetValue(e, out var pace) && (s.Speed != pace || s.GaitSpeed != pace))
                {
                    s.Speed = s.GaitSpeed = pace;
                    EM.SetComponentData(e, s);
                }
                var moved = paradeWas.TryGetValue(e, out var was) ? math.distance(was, s.Position.xz) : 1f;
                if (moved < 0.25f * s.Speed * dt)
                {
                    s.Position.x += math.sin(s.Yaw) * s.Speed * dt;
                    s.Position.z += math.cos(s.Yaw) * s.Speed * dt;
                    s.Position.y = source.SampleHeight(s.Position.xz);
                    EM.SetComponentData(e, s);
                    walked++;
                }
                paradeWas[e] = s.Position.xz;
            }
            return walked;
        }

        /// <summary>Where the front row of the last GaitParade is now: [x, y, z, 1 if still in the world].</summary>
        public static float[] GaitParadeAt()
        {
            if (parade.Count == 0 || !EM.Exists(parade[0]))
                return new float[4];
            var s = EM.GetComponentData<CreatureState>(parade[0]);
            return new[] { s.Position.x, s.Position.y, s.Position.z, s.Gone ? 0f : 1f };
        }

        /// <summary>Sends off the people GaitParade made.</summary>
        public static void GaitParadeClear()
        {
            paradeWas.Clear();
            paradeSpeed.Clear();
            foreach (var e in parade)
                if (EM.Exists(e) && EM.HasComponent<CreatureState>(e))
                {
                    var s = EM.GetComponentData<CreatureState>(e);
                    s.Gone = true;
                    EM.SetComponentData(e, s);
                }
            parade.Clear();
        }
    }
}
#endif
