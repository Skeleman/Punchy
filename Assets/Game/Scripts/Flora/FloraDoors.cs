using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Flora
{
    /// <summary>
    /// Doors that something has opened (a villager going in or out): up to <see cref="Max"/> at once, each by where its
    /// door stands (a Door anchor's position is close enough: the shader matches a leaf it stands in front of, within 15 m, for a stilt house's
    /// anchor at its walkway's end, W-105) and
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

        /// <summary>
        /// P-37 r3 (#369): goes up whenever a door crosses half open (either way) or all are shut, so FloraSystem can
        /// re-gather the walk-in door leaves' boxes at once. Not bumped by small swings, so a door eased open every frame
        /// costs nothing.
        /// </summary>
        public static int Version { get; private set; }

        /// <summary>Opens (or, with 0, shuts) the door at a position. The oldest entry gives way when all are taken.</summary>
        public static void SetOpen(float3 doorPosition, float amount)
        {
            amount = math.saturate(amount);
            for (int i = 0; i < open.Count; i++)
                if (math.distance((float3)(Vector3)open[i], doorPosition) < 0.5f)
                {
                    if (open[i].w >= 0.5f != amount >= 0.5f)
                        Version++;
                    if (amount <= 0f)
                        open.RemoveAt(i);
                    else
                        open[i] = new Vector4(doorPosition.x, doorPosition.y, doorPosition.z, amount);
                    return;
                }
            if (amount <= 0f)
                return;
            if (open.Count == Max && open[0].w >= 0.5f)
                Version++;
            if (open.Count == Max)
                open.RemoveAt(0);
            if (amount >= 0.5f)
                Version++;
            open.Add(new Vector4(doorPosition.x, doorPosition.y, doorPosition.z, amount));
        }

        /// <summary>
        /// P-37 r3 (#369): doors held shut (walk-in houses' shut doors near the player, from Doors every frame): entries
        /// with w = -1, replacing the last frame's. They never push out an opened door: only free entries are used, at
        /// most <see cref="MaxHeld"/>. Flora.hlsl takes the largest matching w, so for now they only keep the slot; with
        /// the render lane's change (a matching w below 0 forces 0) they also stop the daytime ajar rhythm on that leaf.
        /// </summary>
        public static void HoldShut(List<float3> doorPositions)
        {
            open.RemoveAll(d => d.w < 0f);
            var room = math.min(MaxHeld, Max - open.Count);
            for (int i = 0; i < doorPositions.Count && i < room; i++)
                open.Add(new Vector4(doorPositions[i].x, doorPositions[i].y, doorPositions[i].z, -1f));
        }

        public const int MaxHeld = 6;

        /// <summary>P-37 r3 (#369): whether a door within this distance of a point is open (half way or more).</summary>
        public static bool IsOpen(float3 near, float within)
        {
            foreach (var d in open)
                if (d.w >= 0.5f && math.distance((float3)(Vector3)d, near) < within)
                    return true;
            return false;
        }

        /// <summary>Shuts every door it has opened.</summary>
        public static void Clear()
        {
            if (open.Count > 0)
                Version++;
            open.Clear();
        }

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

        /// <summary>
        /// P-37 r3 row 2 (#369): the doors open half way or more, as (x, z), into <paramref name="into"/>; returns how many.
        /// Creatures pass a walk-in house's doorway only while its door is open (Fauna/WalkInWalls).
        /// </summary>
        public static int OpenSnapshot(NativeArray<float2> into)
        {
            var n = 0;
            for (int i = 0; i < open.Count && n < into.Length; i++)
                if (open[i].w >= 0.5f)
                    into[n++] = new float2(open[i].x, open[i].z);
            return n;
        }
    }
}
