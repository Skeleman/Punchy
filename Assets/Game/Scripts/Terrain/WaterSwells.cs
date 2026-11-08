using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// Sea swell (prototype, roadmap 3.4, branch water/swells): the game-side twin of WaterSwell.hlsl, so a swimmer rides
    /// the swell the sea is drawn with, and the camera knows when a crest has closed over it. Height only: the drawn
    /// surface also slides a little along each wave, which this leaves out (a swimmer's bob is the part that shows).
    /// The state (on, sea level, wind, clock) is set once a frame on the main thread (Underwater.cs) and read from Burst.
    /// This class is the one source of the wave table and the swell's shape: <see cref="Publish"/> uploads them as
    /// shader globals (_PunchySwellWaves, _PunchySwellShape), so the drawn sea and the swimmer can't drift apart
    /// (refactor 3.4, audit section 2). WaterSwell.hlsl keeps only the maths.
    /// Prototype limits: creatures, buoys, driftwood and boats still float at the flat level; the wind is the camera's,
    /// so in multiplayer each player's swell height follows their own wind.
    /// </summary>
    public static class WaterSwells
    {
        /// <summary>-noswells on the command line: a flat sea, as before, to compare.</summary>
        public static readonly bool Enabled = !System.Environment.CommandLine.Contains("-noswells");

        /// <summary>What Burst code reads, kept apart: Burst can't run this class's managed static setup.</summary>
        static class Shared
        {
            struct StateKey { }
            struct TableKey { }
            /// <summary>x 1 on / 0 off, y the sea level, z the wind 0..1, w the shared clock (s).</summary>
            public static readonly SharedStatic<float4> State = SharedStatic<float4>.GetOrCreate<StateKey>();
            /// <summary>The wave table, as in Table.</summary>
            public static readonly SharedStatic<FixedList4096Bytes<float4>> Table = SharedStatic<FixedList4096Bytes<float4>>.GetOrCreate<TableKey>();
        }

        static readonly int SwellId = Shader.PropertyToID("_PunchySwell");
        static readonly int WindId = Shader.PropertyToID("_PunchyWind");
        static readonly int ClockId = Shader.PropertyToID("_PunchyClock");
        static readonly int WavesId = Shader.PropertyToID("_PunchySwellWaves");
        static readonly int ShapeId = Shader.PropertyToID("_PunchySwellShape");

        /// <summary>The shader's room for waves (PunchySwellMaxWaves in WaterSwell.hlsl: the global array's size only).
        /// Unity fixes a global array's length the first time it's set, so the upload is always this long.</summary>
        public const int MaxWaves = 12;

        // The waves: wavelength (m), direction (radians), relative height, phase.
        static readonly float4[] Table =
        {
            new float4(38f, 0.60f, 1.00f, 0.0f),
            new float4(23f, 1.05f, 0.55f, 1.7f),
            new float4(14f, 0.25f, 0.32f, 4.1f),
            new float4(9f, 1.40f, 0.18f, 2.6f),
        };

        /// <summary>The swell's height (m) at full wind, before each wave's relative height (PunchySwellHeight).</summary>
        const float CalmHeight = 0.18f, WindHeight = 0.9f;
        /// <summary>How far each wave carries a point along it, as a fraction of its height (the Gerstner 0.6).</summary>
        const float Along = 0.6f;
        /// <summary>Extra room (m) round the furthest the swell can reach, for the render bounds.</summary>
        const float BoundsMargin = 0.5f;

        /// <summary>
        /// The furthest the swell can move a point of the sea's surface (m: x and z sideways, y up or down), with a
        /// margin, worked out from the wave table (refactor 3.4, audit section 2: the bounds were the flat mesh's, so a
        /// heaving chunk at a frustum edge could be culled while still in view). TerrainStreamingSystem pads every water
        /// chunk's and region's render bounds by it.
        /// </summary>
        public static readonly float3 BoundsPad = ComputeBoundsPad();

        static float3 ComputeBoundsPad()
        {
            var total = 0f;
            foreach (var w in Table)
                total += w.z;
            var height = (CalmHeight + WindHeight) * total;
            return new float3(Along * height, height, Along * height) + BoundsMargin;
        }

        /// <summary>Sets this frame's state for the shader and for Burst code (main thread, once a frame).</summary>
        public static void Publish(float seaLevel)
        {
            var clock = Shader.GetGlobalFloat(ClockId);
            var state = new float4(Enabled ? 1f : 0f, seaLevel, Shader.GetGlobalFloat(WindId), clock > 0f ? clock : Time.time);
            Shared.State.Data = state;
            Shader.SetGlobalVector(SwellId, new Vector4(state.x, state.y, state.z, 0f));
            if (!built)
                Build();
            // (Every frame, cheap: nothing else may have set them since, but globals are easily lost in the editor.)
            Shader.SetGlobalVectorArray(WavesId, upload);
            Shader.SetGlobalVector(ShapeId, new Vector4(CalmHeight, WindHeight, Along, Table.Length));
        }

        static bool built;
        static readonly Vector4[] upload = new Vector4[MaxWaves];

        /// <summary>The wave table for Burst and for the shader upload (once: it never changes in a run). Padding
        /// entries have no height, so the shader can loop over all of them.</summary>
        static void Build()
        {
            built = true;
            Debug.Assert(Table.Length <= MaxWaves, "WaterSwells: more waves than the shader has room for");
            var list = new FixedList4096Bytes<float4>();
            for (int i = 0; i < MaxWaves; i++)
            {
                var w = i < Table.Length ? Table[i] : new float4(10f, 0f, 0f, 0f);
                list.Add(w);
                upload[i] = new Vector4(w.x, w.y, w.z, w.w);
            }
            Shared.Table.Data = list;
        }

        /// <summary>
        /// How far the swell lifts the water surface at xz (m; 0 off the sea or with -noswells), for water whose flat
        /// surface is <paramref name="surface"/> over a bed at <paramref name="bed"/>. Same maths as PunchySwell.
        /// </summary>
        public static float Height(float2 xz, float surface, float bed)
        {
            var s = Shared.State.Data;
            if (s.x <= 0f || math.abs(surface - s.y) > 0.05f)
                return 0f;
            var amount = math.smoothstep(0.1f, 2.5f, surface - bed);
            if (amount <= 0f)
                return 0f;
            var height = (CalmHeight + WindHeight * math.saturate(s.z)) * amount;
            var table = Shared.Table.Data;
            var y = 0f;
            for (int i = 0; i < table.Length; i++)
            {
                var w = table[i];
                var k = 2f * math.PI / w.x;
                var dir = new float2(math.cos(w.y), math.sin(w.y));
                var theta = k * math.dot(dir, xz) - math.sqrt(9.81f * k) * s.w + w.w;
                y += height * w.z * math.sin(theta);
            }
            return y;
        }
    }
}
