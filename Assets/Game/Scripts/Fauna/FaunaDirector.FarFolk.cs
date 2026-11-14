using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>A town of houses seen from afar (A-109, A-110): its plan, and how its far figures are being drawn.</summary>
    public sealed class FarTown
    {
        public int Landmark;
        public string Name;
        public float3 Centre;
        public float Radius;
        /// <summary>Its people and their places: what Whereabouts reads.</summary>
        public TownPlan Plan;
        /// <summary>Whether its real folk are in the world on this machine just now (the far figures then give way to
        /// them near the camera).</summary>
        public bool Real;
        internal float RealBlend;
        internal bool Drawn;
        /// <summary>The largest a far figure of this town has looked on screen while shown (reference pixels; ruling 5).</summary>
        public float MaxPixels;
        /// <summary>The same since the last log line (the -farfolk log resets it).</summary>
        public float NowPixels;
        /// <summary>The last frame it was drawn: the day and hour, how many members Whereabouts put outdoors (Placed),
        /// how many of those were drawn after the fades (Shown), how many were left out as dead (Gone), and how many
        /// were waiting for their day to be built (Unbuilt). StatFrame is that frame's number (-1: never drawn).</summary>
        public int StatDay, Placed, Shown, Gone, Unbuilt, StatFrame = -1;
        public float StatHour;
        /// <summary>The -farfolk log's figures for a day: the most outdoors at once, when, and the day's digest.</summary>
        internal int PeakDay = int.MinValue, Peak;
        internal float PeakHour;
        internal uint Digest;
        internal bool MadeLogged;
    }

    /// <summary>
    /// The far figures of every town (A-109 row 2, A-110 row 2): drawn only, so a town seen from a ridge has people
    /// in it. Nothing is simulated, sent or stored: every household member who is outdoors by Whereabouts is drawn
    /// where Whereabouts puts it, so every machine draws the same figures. FaunaDirector builds the towns' plans;
    /// CreatureRenderSystem draws them.
    /// </summary>
    public static class FarFolk
    {
        /// <summary>How tall a grown figure stands (m): what its on-screen size is measured from.</summary>
        public const float FigureHeight = 1.7f;
        public static readonly List<FarTown> Towns = new List<FarTown>();
        /// <summary>Whether every town's plan has been built (they are built one at a time after the world loads).</summary>
        public static bool Complete;

        internal static void Clear()
        {
            Towns.Clear();
            Complete = false;
        }

        public static float Hash01(uint seed, int k) =>
            (BiomeSampling.Scramble(seed ^ (uint)(k + 1) * 0x9E3779B9u) & 0xFFFFFFu) / 16777216f;
    }

    // A-109 row 2, A-110 row 2: has each town's plan built (FaunaDirector.TownPlan) and tells whether its real folk
    // are about. On every machine (a joined client too): all of it is derived, none of it touches the simulation, the
    // spawn draws or the network.
    public partial class FaunaDirector
    {
        readonly HashSet<int> farReal = new HashSet<int>();
        EntityQuery farQuery;
        World farQueryWorld;
        float nextFarWatch;
        int farNext, farKey;

        /// <summary>
        /// A few times a second: builds a little more of the next town's plan (so no frame pays for a whole town) and
        /// marks the towns whose real folk are in the world here.
        /// </summary>
        void WatchFarFolk(in TerrainSource source)
        {
            if (Time.time < nextFarWatch || !built || !source.Landmarks.IsCreated || queryWorld == null || !queryWorld.IsCreated)
                return;
            nextFarWatch = Time.time + 0.2f;
            EnsureHouses(source);
            if (!Households.Ready)
                return;
            ref var set = ref source.Landmarks.Value;
            // (Another island, or the households rebuilt: start over.)
            var key = source.Landmarks.GetHashCode() ^ Households.All.Count * 7919;
            if (key != farKey)
            {
                farKey = key;
                farNext = 0;
                planPending = null;
                FarFolk.Clear();
                Whereabouts.Clear();
            }
            for (int tries = 0; planPending == null && tries < 24 && farNext < set.Instances.Length; tries++)
            {
                planPending = StartTownPlan(source, farNext++);
                planCursor = 0;
            }
            if (planPending != null && FillTownGrid(source, planPending, ref planCursor, 600))
            {
                var plan = planPending;
                planPending = null;
                if (FinishTownPlan(source, plan))
                {
                    ref var inst = ref set.Instances[plan.Landmark];
                    Whereabouts.Towns[plan.Landmark] = plan;
                    FarFolk.Towns.Add(new FarTown
                    {
                        Landmark = plan.Landmark,
                        Name = plan.Name,
                        Centre = new float3(inst.Position.x, inst.BaseHeight, inst.Position.y),
                        Radius = plan.Radius,
                        Plan = plan
                    });
                }
            }
            FarFolk.Complete = planPending == null && farNext >= set.Instances.Length;

            if (farQueryWorld != queryWorld)
            {
                farQueryWorld = queryWorld;
                farQuery = queryWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>());
            }
            farReal.Clear();
            var states = farQuery.ToComponentDataArray<CreatureState>(Allocator.Temp);
            foreach (var s in states)
                if (s.Individual != 0 && Households.Of(s.Individual, out var house, out _))
                    farReal.Add(house.Landmark);
            states.Dispose();
            foreach (var town in FarFolk.Towns)
                town.Real = farReal.Contains(town.Landmark);
        }

        /// <summary>The far figures' part of Teardown.</summary>
        void TeardownFarFolk()
        {
            FarFolk.Clear();
            Whereabouts.Clear();
            planPending = null;
            farKey = 0;
            farNext = 0;
            farQueryWorld = null;
            farQuery = default;
        }

        /// <summary>The ground a far figure stands on: the terrain, or wading depth where that is under water.</summary>
        static float FarGround(in TerrainSource source, float2 at)
        {
            var g = source.SampleGround(at);
            return g.HasWater && g.WaterSurface > g.Height ? math.max(g.Height, g.WaterSurface - 0.4f) : g.Height;
        }

        /// <summary>A figure's body and limb colours: its species' (one of its coats, picked by its seed).</summary>
        static void FarColours(CreatureSpecies species, uint seed, out float4 body, out float4 limbs)
        {
            var b = species.Base;
            var a = species.Accent;
            if (species.Coats != null && species.Coats.Count > 0)
            {
                var coat = species.Coats[(int)(BiomeSampling.Scramble(seed ^ 0xC0A7u) % (uint)species.Coats.Count)];
                b = coat.Base;
                a = coat.Accent;
            }
            body = new float4(b.linear.r, b.linear.g, b.linear.b, 0f);
            limbs = new float4(a.linear.r, a.linear.g, a.linear.b, 0f);
        }
    }
}
