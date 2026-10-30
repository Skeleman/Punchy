using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// Tells the water where things have been swimming, for its wakes and rings (Water.shader). Anything at the surface
    /// near the camera (fish near the top, ducks, animals and folk swimming or wading, players' bodies) drops a ripple
    /// where it is every half metre it goes, or every half second keeping still. Each ripple stays where it was made,
    /// a ring spreading out from there and dying away over RippleTime. A row of them trailing behind a swimmer is its
    /// wake, left in the water rather than carried round with it as it turns. The 48 nearest the camera go out as
    /// _PunchySwimmers (x world x, y its age 0..1, z world z, w how strongly it was stirred, 0 an empty slot) and
    /// _PunchySwimmerCount. From state every machine already has, so everyone's water shows much the same; nothing is
    /// sent.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial class CreatureWakeSystem : SystemBase
    {
        public const int MaxSwimmers = 48;
        /// <summary>How long a ripple spreads before it's gone (s); a new one each Spacing metres gone, or each Still
        /// seconds keeping still.</summary>
        const float RippleTime = 3f, Spacing = 0.5f, Still = 0.5f;
        /// <summary>How far from the camera swimmers are sent (m); and the band round the surface they're in to count
        /// (above it, for a duck; below, for a fish near the top or a swimmer's feet).</summary>
        const float Range = 40f, Above = 0.8f, Below = 1.6f;

        static readonly int SwimmersId = Shader.PropertyToID("_PunchySwimmers");
        static readonly int CountId = Shader.PropertyToID("_PunchySwimmerCount");
        readonly Vector4[] slots = new Vector4[MaxSwimmers];
        readonly float[] distances = new float[MaxSwimmers];
        /// <summary>Whether each creature was at the water's surface when last looked (a sixth of them each frame).</summary>
        readonly System.Collections.Generic.Dictionary<Entity, bool> wet = new System.Collections.Generic.Dictionary<Entity, bool>();
        const int Stagger = 6;
        EntityQuery query;

        struct Ripple
        {
            public float2 At;
            public float Born, Strength;
        }

        readonly System.Collections.Generic.List<Ripple> ripples = new System.Collections.Generic.List<Ripple>();
        /// <summary>Per swimmer: where and when it dropped its last ripple.</summary>
        readonly System.Collections.Generic.Dictionary<Entity, float3> lastRipple = new System.Collections.Generic.Dictionary<Entity, float3>();

        protected override void OnCreate()
        {
            query = GetEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
        }

        protected override void OnUpdate()
        {
            var cam = Camera.main;
            var count = 0;
            var now = UnityEngine.Time.time;
            ripples.RemoveAll(r => now - r.Born >= RippleTime);
            if (cam != null && SystemAPI.TryGetSingleton<TerrainSource>(out var terrain))
            {
                float3 camAt = cam.transform.position;
                var frame = UnityEngine.Time.frameCount;
                var entities = query.ToEntityArray(Allocator.Temp);
                var states = query.ToComponentDataArray<CreatureState>(Allocator.Temp);
                var bodies = query.ToComponentDataArray<CreatureBody>(Allocator.Temp);
                for (int i = 0; i < states.Length; i++)
                {
                    var s = states[i];
                    var d = math.distance(s.Position, camAt);
                    if (d > Range || s.Gone || s.Fly > 0.5f)
                        continue;
                    // (Only what can be in the water: swimmers, floaters, animals that cross it, folk, players' bodies.)
                    ref var b = ref bodies[i].Blob.Value;
                    if (b.Habitat != Habitat.Swims && b.Habitat != Habitat.Floats && !b.CrossesWater && !b.Upright && s.Mode != CreatureMode.Puppet)
                        continue;
                    // Whether it's at the surface, looked at again every few frames (the ground sample is the cost):
                    // in between, where it is now goes out as last seen wet or not.
                    var e = entities[i];
                    if (!wet.TryGetValue(e, out var atSurface) || (e.Index + frame) % Stagger == 0)
                    {
                        var g = terrain.SampleGround(s.Position.xz);
                        var under = g.HasWater ? g.WaterSurface - s.Position.y : float.MaxValue;
                        atSurface = g.HasWater && g.WaterSurface - g.Height >= 0.15f && under >= -Above && under <= Below;
                        wet[e] = atSurface;
                    }
                    if (!atSurface)
                    {
                        lastRipple.Remove(e);
                        continue;
                    }
                    // A ripple where it is, every half metre or half second: stronger the quicker it goes, and the bigger.
                    if (!lastRipple.TryGetValue(e, out var last) || math.distance(last.xy, s.Position.xz) >= Spacing || now - last.z >= Still)
                    {
                        lastRipple[e] = new float3(s.Position.xz, now);
                        var strength = math.clamp(0.3f + s.Speed * 0.35f, 0.3f, 1.6f) * math.clamp(s.Scale * b.FrontRadius * 6f, 0.5f, 1.5f);
                        ripples.Add(new Ripple { At = s.Position.xz, Born = now, Strength = strength });
                    }
                }
                foreach (var r in ripples)
                {
                    var d = math.distance(r.At, camAt.xz);
                    if (d > Range)
                        continue;
                    // (Kept to the nearest 48: a full list gives way to anything nearer than its furthest.)
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
                    slots[slot] = new Vector4(r.At.x, (now - r.Born) / RippleTime, r.At.y, r.Strength);
                }
                states.Dispose();
                bodies.Dispose();
                entities.Dispose();
                if (wet.Count > 2000)
                    wet.Clear();
                if (lastRipple.Count > 500)
                    lastRipple.Clear();
            }
            for (int k = count; k < MaxSwimmers; k++)
                slots[k] = Vector4.zero;
            Shader.SetGlobalVectorArray(SwimmersId, slots);
            Shader.SetGlobalInteger(CountId, count);
        }
    }
}
