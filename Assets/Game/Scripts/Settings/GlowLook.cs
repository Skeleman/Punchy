using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Settings
{
    /// <summary>
    /// R-39 row 2 (#402): night looks for glowing things to compare, so a glow (igloo runes, glowing mushrooms) reads as
    /// light falling on the ground and walls round it. Each option darkens the night (the ambient and the moons) and
    /// changes how a glow slot falls off (Glow.hlsl: brightness, reach and the falloff's power). Option 0 is today's look.
    /// Picked with the launch flag "-glowlook n" or at run time (Option); the pick is Zach's (STOP point of the card).
    /// </summary>
    public static class GlowLook
    {
        /// <summary>The look in use: 0 today, 1 dark nights only, 2 dark nights and a wider, brighter wash,
        /// 3 darker nights and a bright pool close to each glow.</summary>
        public static int Option;

        /// <summary>Per option: x the night's ambient and moonlight kept, y glow brightness, z glow reach scale,
        /// w the falloff's power (2 today: (1 - d / reach)^2).</summary>
        static readonly float4[] Options =
        {
            new float4(1f, 1f, 1f, 2f),
            new float4(0.5f, 1f, 1f, 2f),
            new float4(0.5f, 1.6f, 1.3f, 2f),
            new float4(0.35f, 2.6f, 1.15f, 3f),
        };

        public static float4 Current => Options[math.clamp(Option, 0, Options.Length - 1)];

        /// <summary>How much of the night's light (ambient, moons) is kept at this much night (0 day, 1 full night).</summary>
        public static float NightScale(float night) => math.lerp(1f, Current.x, math.saturate(night));

        static readonly int GlowLookId = Shader.PropertyToID("_PunchyGlowLook");

        /// <summary>Hands the falloff to the shaders (once a frame, CaveSystem).</summary>
        public static void Upload()
        {
            var o = Current;
            Shader.SetGlobalVector(GlowLookId, new Vector4(o.y, 1f / o.z, o.w, 0f));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            var at = System.Array.IndexOf(args, "-glowlook");
            Option = at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var n) ? n : 0;
            Upload();
        }
    }
}
