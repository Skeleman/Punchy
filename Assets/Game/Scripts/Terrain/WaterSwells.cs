using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// Sea swell (prototype, roadmap 3.4, branch water/swells): the game-side twin of WaterSwell.hlsl, so a swimmer rides
    /// the swell the sea is drawn with, and the camera knows when a crest has closed over it. Height only: the drawn
    /// surface also slides a little along each wave, which this leaves out (a swimmer's bob is the part that shows).
    /// The state (on, sea level, wind, clock) is set once a frame on the main thread (Underwater.cs) and read from Burst.
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
            /// <summary>The wave table (columns), as in Table.</summary>
            public static readonly SharedStatic<float4x4> Table = SharedStatic<float4x4>.GetOrCreate<TableKey>();
        }

        static readonly int SwellId = Shader.PropertyToID("_PunchySwell");
        static readonly int WindId = Shader.PropertyToID("_PunchyWind");
        static readonly int ClockId = Shader.PropertyToID("_PunchyClock");

        // Must match PunchySwellTable in WaterSwell.hlsl: wavelength (m), direction (radians), relative height, phase.
        static readonly float4[] Table =
        {
            new float4(38f, 0.60f, 1.00f, 0.0f),
            new float4(23f, 1.05f, 0.55f, 1.7f),
            new float4(14f, 0.25f, 0.32f, 4.1f),
            new float4(9f, 1.40f, 0.18f, 2.6f),
        };

        /// <summary>Sets this frame's state for the shader and for Burst code (main thread, once a frame).</summary>
        public static void Publish(float seaLevel)
        {
            var clock = Shader.GetGlobalFloat(ClockId);
            var state = new float4(Enabled ? 1f : 0f, seaLevel, Shader.GetGlobalFloat(WindId), clock > 0f ? clock : Time.time);
            Shared.State.Data = state;
            Shared.Table.Data = new float4x4(Table[0], Table[1], Table[2], Table[3]);
            Shader.SetGlobalVector(SwellId, new Vector4(state.x, state.y, state.z, 0f));
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
            var height = (0.18f + 0.9f * math.saturate(s.z)) * amount;
            var table = Shared.Table.Data;
            var y = 0f;
            for (int i = 0; i < 4; i++)
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
