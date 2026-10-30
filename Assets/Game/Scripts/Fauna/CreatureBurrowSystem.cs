using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// Tells the ground where worms are tunnelling, for the ripple over each one's head and the furrow behind it, and
    /// where they've broken out of it or dived back in, for a crater there (the world session's TerrainSurface.shader):
    /// the 8 nearest the camera within Range, as _PunchyBurrowers and _PunchyBurrowerCount. A worm under the ground is
    /// (x world x, y heading in radians, z world z, w its body's radius in metres); a crater is (x, y its age 0..1 over
    /// CraterTime, z, w minus the radius of the worm that made it); 0 an empty slot. While a worm is out in its arc it
    /// has no slot of its own: its body is there to be seen. From state every machine already has (Air is on the
    /// creature ghost); nothing is sent. A player who joins late misses craters made before it came.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial class CreatureBurrowSystem : SystemBase
    {
        public const int MaxBurrowers = 8;
        /// <summary>How far from the camera a worm's ripple is shown (they're seen from far off).</summary>
        const float Range = 250f;
        /// <summary>How long a crater stays (s), fading as its age runs to 1.</summary>
        const float CraterTime = 6f;

        static readonly int BurrowersId = Shader.PropertyToID("_PunchyBurrowers");
        static readonly int CountId = Shader.PropertyToID("_PunchyBurrowerCount");
        readonly Vector4[] slots = new Vector4[MaxBurrowers];
        readonly float[] distances = new float[MaxBurrowers];
        int count;
        EntityQuery query;

        struct Crater
        {
            public float2 At;
            public float Radius;
            public float Born;
        }

        readonly List<Crater> craters = new List<Crater>();
        /// <summary>Which worms were out of the ground last frame (to catch the frames they break out and dive back in).</summary>
        Dictionary<Entity, bool> wasOut = new Dictionary<Entity, bool>();
        Dictionary<Entity, bool> nowOut = new Dictionary<Entity, bool>();

        protected override void OnCreate()
        {
            query = GetEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
        }

        protected override void OnUpdate()
        {
            var cam = Camera.main;
            var now = UnityEngine.Time.time;
            count = 0;
            craters.RemoveAll(c => now - c.Born >= CraterTime);
            if (cam != null)
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
                    var radius = math.max(b.FrontRadius * s.Scale, 0.3f);
                    var outNow = s.Air > 0f;
                    nowOut[entities[i]] = outNow;
                    // Breaking out of the ground, or diving back in (a worm first seen mid-arc leaves no crater where
                    // it came out, only where it goes back in).
                    if (wasOut.TryGetValue(entities[i], out var before) && before != outNow)
                        craters.Add(new Crater { At = s.Position.xz, Radius = radius, Born = now });
                    if (!outNow)
                        Offer(new Vector4(s.Position.x, s.Yaw, s.Position.z, radius), math.distance(s.Position.xz, camAt.xz));
                }
                (wasOut, nowOut) = (nowOut, wasOut);
                entities.Dispose();
                states.Dispose();
                bodies.Dispose();
                foreach (var c in craters)
                    Offer(new Vector4(c.At.x, (now - c.Born) / CraterTime, c.At.y, -c.Radius), math.distance(c.At, camAt.xz));
            }
            for (int k = count; k < MaxBurrowers; k++)
                slots[k] = Vector4.zero;
            Shader.SetGlobalVectorArray(BurrowersId, slots);
            Shader.SetGlobalInteger(CountId, count);
        }

        /// <summary>Into a slot if it's within Range and among the 8 nearest the camera so far.</summary>
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
