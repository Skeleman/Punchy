using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// Tells the ground where worms are tunnelling, for the ripple over each one's head, the furrow behind it and the
    /// crater where it breaks out (the world session's TerrainSurface.shader): the 8 nearest the camera within Range, as
    /// _PunchyBurrowers (x world x, y heading in radians, z world z, w its body's radius in metres, negative while it's
    /// out of the ground; 0 an empty slot) and _PunchyBurrowerCount. From state every machine already has; nothing is sent.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial class CreatureBurrowSystem : SystemBase
    {
        public const int MaxBurrowers = 8;
        /// <summary>How far from the camera a worm's ripple is shown (they're seen from far off).</summary>
        const float Range = 250f;

        static readonly int BurrowersId = Shader.PropertyToID("_PunchyBurrowers");
        static readonly int CountId = Shader.PropertyToID("_PunchyBurrowerCount");
        readonly Vector4[] slots = new Vector4[MaxBurrowers];
        readonly float[] distances = new float[MaxBurrowers];
        EntityQuery query;

        protected override void OnCreate()
        {
            query = GetEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
        }

        protected override void OnUpdate()
        {
            var cam = Camera.main;
            var count = 0;
            if (cam != null)
            {
                float3 camAt = cam.transform.position;
                var states = query.ToComponentDataArray<CreatureState>(Allocator.Temp);
                var bodies = query.ToComponentDataArray<CreatureBody>(Allocator.Temp);
                for (int i = 0; i < states.Length; i++)
                {
                    ref var b = ref bodies[i].Blob.Value;
                    if (!b.Tunnels)
                        continue;
                    var s = states[i];
                    var d = math.distance(s.Position.xz, camAt.xz);
                    if (d > Range || s.Gone)
                        continue;
                    // (Kept to the nearest 8.)
                    var slot = count;
                    if (count == MaxBurrowers)
                    {
                        slot = 0;
                        for (int k = 1; k < MaxBurrowers; k++)
                            if (distances[k] > distances[slot])
                                slot = k;
                        if (distances[slot] <= d)
                            continue;
                    }
                    else
                        count++;
                    distances[slot] = d;
                    var radius = math.max(b.FrontRadius * s.Scale, 0.3f);
                    slots[slot] = new Vector4(s.Position.x, s.Yaw, s.Position.z, s.Air > 0f ? -radius : radius);
                }
                states.Dispose();
                bodies.Dispose();
            }
            for (int k = count; k < MaxBurrowers; k++)
                slots[k] = Vector4.zero;
            Shader.SetGlobalVectorArray(BurrowersId, slots);
            Shader.SetGlobalInteger(CountId, count);
        }
    }
}
