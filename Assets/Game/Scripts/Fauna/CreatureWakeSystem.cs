using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// Tells the water where things are swimming, for its wakes and rings (Water.shader): the 16 creatures nearest the
    /// camera that are at the surface (fish near the top, ducks, animals and folk swimming or wading, players' bodies),
    /// as _PunchySwimmers (x world x, y heading in radians, z world z, w speed in m/s, 0 for an empty slot) and
    /// _PunchySwimmerCount. From state every machine already has, so everyone's water shows the same; nothing is sent.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial class CreatureWakeSystem : SystemBase
    {
        public const int MaxSwimmers = 16;
        /// <summary>How far from the camera swimmers are sent (m); and the band round the surface they're in to count
        /// (above it, for a duck; below, for a fish near the top or a swimmer's feet).</summary>
        const float Range = 40f, Above = 0.8f, Below = 1.6f;

        static readonly int SwimmersId = Shader.PropertyToID("_PunchySwimmers");
        static readonly int CountId = Shader.PropertyToID("_PunchySwimmerCount");
        readonly Vector4[] slots = new Vector4[MaxSwimmers];
        readonly float[] distances = new float[MaxSwimmers];
        EntityQuery query;

        protected override void OnCreate()
        {
            query = GetEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
        }

        protected override void OnUpdate()
        {
            var cam = Camera.main;
            var count = 0;
            if (cam != null && SystemAPI.TryGetSingleton<TerrainSource>(out var terrain))
            {
                float3 camAt = cam.transform.position;
                var states = query.ToComponentDataArray<CreatureState>(Allocator.Temp);
                for (int i = 0; i < states.Length; i++)
                {
                    var s = states[i];
                    var d = math.distance(s.Position, camAt);
                    if (d > Range || s.Gone || s.Fly > 0.5f)
                        continue;
                    var g = terrain.SampleGround(s.Position.xz);
                    var under = g.HasWater ? g.WaterSurface - s.Position.y : float.MaxValue;
                    if (!g.HasWater || g.WaterSurface - g.Height < 0.15f || under < -Above || under > Below)
                        continue;
                    // (Kept to the nearest 16: a full list gives way to anything nearer than its furthest.)
                    var slot = count;
                    if (count == MaxSwimmers)
                    {
                        slot = 0;
                        for (int k = 1; k < MaxSwimmers; k++)
                            if (distances[k] > distances[slot])
                                slot = k;
                        if (distances[slot] <= d)
                            continue;
                    }
                    else
                        count++;
                    distances[slot] = d;
                    // (Still, a whisker over 0, so the slot reads as used: rings only.)
                    slots[slot] = new Vector4(s.Position.x, s.Yaw, s.Position.z, math.max(s.Speed, 0.01f));
                }
                states.Dispose();
            }
            for (int k = count; k < MaxSwimmers; k++)
                slots[k] = Vector4.zero;
            Shader.SetGlobalVectorArray(SwimmersId, slots);
            Shader.SetGlobalInteger(CountId, count);
        }
    }
}
