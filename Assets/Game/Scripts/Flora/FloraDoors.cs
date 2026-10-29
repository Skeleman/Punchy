using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Flora
{
    /// <summary>
    /// Doors that something has opened (a villager going in or out): up to <see cref="Max"/> at once, each by where its
    /// door stands (a Door anchor's position is close enough: the shader matches a leaf by its hinge within 2 m) and
    /// how far open (0 shut, 1 wide open). Flora.hlsl swings those leaves (FloraPart.Door) about their hinges; with
    /// nothing set, doors keep their own rhythm (a few stand ajar by day, all shut at night). Cosmetic and per machine:
    /// callers set it on every machine from what they already know (the residents' positions), so nothing is sent.
    /// </summary>
    public static class FloraDoors
    {
        public const int Max = 16;
        static readonly List<Vector4> open = new List<Vector4>();
        static readonly Vector4[] upload = new Vector4[Max];
        static readonly int OpenId = Shader.PropertyToID("_PunchyDoorOpen");
        static readonly int CountId = Shader.PropertyToID("_PunchyDoorCount");
        static readonly int DayId = Shader.PropertyToID("_PunchyDoorDay");
        static readonly int NightId = Shader.PropertyToID("_PunchyNight");

        /// <summary>Opens (or, with 0, shuts) the door at a position. The oldest entry gives way when all are taken.</summary>
        public static void SetOpen(float3 doorPosition, float amount)
        {
            amount = math.saturate(amount);
            for (int i = 0; i < open.Count; i++)
                if (math.distance((float3)(Vector3)open[i], doorPosition) < 0.5f)
                {
                    if (amount <= 0f)
                        open.RemoveAt(i);
                    else
                        open[i] = new Vector4(doorPosition.x, doorPosition.y, doorPosition.z, amount);
                    return;
                }
            if (amount <= 0f)
                return;
            if (open.Count == Max)
                open.RemoveAt(0);
            open.Add(new Vector4(doorPosition.x, doorPosition.y, doorPosition.z, amount));
        }

        /// <summary>Shuts every door it has opened.</summary>
        public static void Clear() => open.Clear();

        /// <summary>Hands the list to the shaders (FloraSystem, every frame).</summary>
        internal static void Upload()
        {
            for (int i = 0; i < Max; i++)
                upload[i] = i < open.Count ? open[i] : Vector4.zero;
            Shader.SetGlobalVectorArray(OpenId, upload);
            // (An int in the shader: SetGlobalInt would set a float, which it reads as 0.)
            Shader.SetGlobalInteger(CountId, open.Count);
            // Daylight for the doors' own rhythm (the sky's night, which flora passes don't all include).
            Shader.SetGlobalFloat(DayId, 1f - Shader.GetGlobalFloat(NightId));
        }
    }
}
