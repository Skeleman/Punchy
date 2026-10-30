using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// Tells the ground where worms have been tunnelling, for the world session's TerrainSurface.shader: a mound over
    /// each one's head and a trail of bumps it leaves behind, each staying where it was raised (TrailTime), and a crater
    /// where one has broken out of the ground or dived back in (CraterTime). The 64 nearest the camera within Range go
    /// out as _PunchyBurrowers and _PunchyBurrowerCount, each (x world x, y its age 0..1, z world z, w the radius of the
    /// worm that made it, in metres: positive a bump, negative a crater); 0 an empty slot. While a worm is out in its
    /// arc it raises no bumps: its body is there to be seen. From state every machine already has (positions are on
    /// the creature ghost); nothing is sent. A player who joins late misses marks made before it came.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial class CreatureBurrowSystem : SystemBase
    {
        public const int MaxBurrowers = 64;
        /// <summary>How far from the camera a worm's marks are shown (they're seen from far off).</summary>
        const float Range = 250f;
        /// <summary>How long a bump in a worm's trail stays (s), sinking away as its age runs to 1.</summary>
        const float TrailTime = 30f;
        /// <summary>How long a crater stays (s), fading as its age runs to 1.</summary>
        const float CraterTime = 40f;
        /// <summary>A new bump each time a worm has gone this many of its radii from the last.</summary>
        const float BumpSpacing = 2f;

        static readonly int BurrowersId = Shader.PropertyToID("_PunchyBurrowers");
        static readonly int CountId = Shader.PropertyToID("_PunchyBurrowerCount");
        readonly Vector4[] slots = new Vector4[MaxBurrowers];
        readonly float[] distances = new float[MaxBurrowers];
        int count;
        EntityQuery query;

        struct Mark
        {
            public float2 At;
            public float Radius;
            public float Born;
        }

        readonly List<Mark> bumps = new List<Mark>();
        readonly List<Mark> craters = new List<Mark>();
        /// <summary>Per worm: whether its body was above the ground last frame (to catch where it breaks out and dives
        /// back in), and where it raised its last bump.</summary>
        Dictionary<Entity, bool> wasOut = new Dictionary<Entity, bool>();
        Dictionary<Entity, bool> nowOut = new Dictionary<Entity, bool>();
        readonly Dictionary<Entity, float2> lastBump = new Dictionary<Entity, float2>();

        protected override void OnCreate()
        {
            query = GetEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
        }

        protected override void OnUpdate()
        {
            var cam = Camera.main;
            var now = UnityEngine.Time.time;
            count = 0;
            bumps.RemoveAll(m => now - m.Born >= TrailTime);
            craters.RemoveAll(m => now - m.Born >= CraterTime);
            if (cam != null && SystemAPI.TryGetSingleton<TerrainSource>(out var terrain))
            {
                float3 camAt = cam.transform.position;
                var entities = query.ToEntityArray(Allocator.Temp);
                var states = query.ToComponentDataArray<CreatureState>(Allocator.Temp);
                var bodies = query.ToComponentDataArray<CreatureBody>(Allocator.Temp);
                nowOut.Clear();
                for (int i = 0; i < states.Length; i++)
                {
                    ref var b = ref bodies[i].Blob.Value;
                    if (!b.Tunnels)
                        continue;
                    var s = states[i];
                    if (s.Gone)
                        continue;
                    var e = entities[i];
                    var radius = math.max(b.FrontRadius * s.Scale, 0.3f);
                    // Out where its body crosses the ground, so the crater is the hole it comes up through and goes
                    // back down (the brain's arc starts and ends deep under it; a worm first seen mid-arc leaves no
                    // crater where it came out, only where it goes back in).
                    var outNow = s.Position.y > terrain.SampleHeight(s.Position.xz);
                    nowOut[e] = outNow;
                    if (wasOut.TryGetValue(e, out var before) && before != outNow)
                        craters.Add(new Mark { At = s.Position.xz, Radius = radius, Born = now });
                    if (outNow)
                    {
                        lastBump.Remove(e);
                        continue;
                    }
                    // Its trail: a bump left behind every couple of radii, where it stays; and the mound over its head.
                    if (!lastBump.TryGetValue(e, out var last) || math.distance(last, s.Position.xz) >= radius * BumpSpacing)
                    {
                        lastBump[e] = s.Position.xz;
                        bumps.Add(new Mark { At = s.Position.xz, Radius = radius, Born = now });
                    }
                    Offer(new Vector4(s.Position.x, 0f, s.Position.z, radius), math.distance(s.Position.xz, camAt.xz));
                }
                (wasOut, nowOut) = (nowOut, wasOut);
                // (Worms gone from view: forget them, keeping their marks.)
                if (lastBump.Count > wasOut.Count)
                {
                    var stale = new List<Entity>();
                    foreach (var kv in lastBump)
                        if (!wasOut.ContainsKey(kv.Key))
                            stale.Add(kv.Key);
                    foreach (var e in stale)
                        lastBump.Remove(e);
                }
                entities.Dispose();
                states.Dispose();
                bodies.Dispose();
                foreach (var m in bumps)
                    Offer(new Vector4(m.At.x, (now - m.Born) / TrailTime, m.At.y, m.Radius), math.distance(m.At, camAt.xz));
                foreach (var m in craters)
                    Offer(new Vector4(m.At.x, (now - m.Born) / CraterTime, m.At.y, -m.Radius), math.distance(m.At, camAt.xz));
            }
            for (int k = count; k < MaxBurrowers; k++)
                slots[k] = Vector4.zero;
            Shader.SetGlobalVectorArray(BurrowersId, slots);
            Shader.SetGlobalInteger(CountId, count);
        }

        /// <summary>Into a slot if it's within Range and among the 64 nearest the camera so far.</summary>
        void Offer(Vector4 value, float distance)
        {
            if (distance > Range)
                return;
            var slot = count;
            if (count == MaxBurrowers)
            {
                slot = 0;
                for (int k = 1; k < MaxBurrowers; k++)
                    if (distances[k] > distances[slot])
                        slot = k;
                if (distances[slot] <= distance)
                    return;
            }
            else
                count++;
            distances[slot] = distance;
            slots[slot] = value;
        }
    }
}
