using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// The debug view of HomeSign (A-109 row 3), behind the -homesign launch flag only: it logs a digest of each
    /// landmark's houses over a day (for checking that host and client agree), a line every few seconds of how many
    /// houses show smoke and a lamp now, and draws a marker over each house: a grey column as tall as its smoke is
    /// strong, and a glowing yellow ball as bright as its lamp. The markers are not the real sign: panes, glow and
    /// chimney smoke are R-25's and a later row's, which read the same function.
    /// </summary>
    public partial class CreatureRenderSystem
    {
        struct HomeMark
        {
            public float3 At;
            public float Smoke, Lamp;
        }

        readonly List<HomeMark> homeMarks = new List<HomeMark>();
        readonly List<float> homeHeights = new List<float>();
        readonly List<int> homeTowns = new List<int>();
        bool homeSignOn, homeSignRead;
        int homeLogged = -1;
        float homeLogNext;

        bool HomeSignOn
        {
            get
            {
                if (!homeSignRead)
                {
                    homeSignRead = true;
                    homeSignOn = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-homesign") >= 0;
                }
                return homeSignOn;
            }
        }

        /// <summary>Logs each landmark's digest for today and for a fixed day, once per set of households.</summary>
        void LogHomeSigns(int today)
        {
            homeTowns.Clear();
            homeTowns.AddRange(Households.ByLandmark.Keys);
            homeTowns.Sort();
            foreach (var landmark in homeTowns)
            {
                var which = Households.ByLandmark[landmark];
                var name = "?";
                foreach (var town in FarFolk.Towns)
                    if (town.Landmark == landmark)
                        name = town.Name;
                var now = HomeSign.Digest(Households.All, which, today, out var smoke, out var lamp);
                var fixedDay = HomeSign.Digest(Households.All, which, 7, out _, out _);
                Debug.Log($"[homesign] town {landmark} {name}: {which.Count} houses, day {today} digest {now:X8}, day 7 digest {fixedDay:X8}, " +
                          $"smoke {smoke / which.Count:F2} h and lamp {lamp / which.Count:F2} h a house today");
                // One house spelt out, so the digest can be read: smoke and lamp in tenths, every two hours.
                var line = new System.Text.StringBuilder();
                for (int hour = 0; hour < 24; hour += 2)
                {
                    var sign = HomeSign.Of(Households.All[which[0]], today, hour);
                    line.Append($" {hour:00}h s{math.round(sign.Smoke * 9f):0} l{math.round(sign.Lamp * 9f):0} in{sign.Home}");
                }
                Debug.Log($"[homesign] town {landmark} house 0 ({Households.All[which[0]].Kind}, {Households.All[which[0]].Members.Length} folk):{line}");
            }
        }

        /// <summary>The markers in view (drawn within 1.5 km). Returns how many capsules they need.</summary>
        int GatherHomeSigns(in TerrainSource terrain, float3 cam, float hour)
        {
            homeMarks.Clear();
            if (!HomeSignOn || !Households.Ready)
                return 0;
            var day = skyCycle != null ? skyCycle.Day : 0;
            var houses = Households.All;
            // (Wait for the far towns to be built, so the log can name them: a few seconds at most.)
            if (homeLogged != houses.Count && UnityEngine.Time.timeSinceLevelLoad > 15f)
            {
                homeLogged = houses.Count;
                homeHeights.Clear();
                LogHomeSigns(day);
            }
            if (homeLogged != houses.Count)
                return 0;
            while (homeHeights.Count < houses.Count)
                homeHeights.Add(float.NaN);
            int smoking = 0, lit = 0, near = 0;
            for (int h = 0; h < houses.Count; h++)
            {
                var house = houses[h];
                if (math.distancesq(cam.xz, house.Door.xz) > 1500f * 1500f)
                    continue;
                near++;
                var sign = HomeSign.Of(house, day, hour);
                if (sign.Smoke > 0.05f)
                    smoking++;
                if (sign.Lamp > 0.05f)
                    lit++;
                if (sign.Smoke <= 0.02f && sign.Lamp <= 0.02f)
                    continue;
                // Over the middle of the house: back from its door, above its roof.
                var inward = -new float2(math.sin(house.Door.w), math.cos(house.Door.w));
                var at = house.Door.xz + inward * 3f;
                if (float.IsNaN(homeHeights[h]))
                    homeHeights[h] = terrain.SampleHeight(at);
                homeMarks.Add(new HomeMark { At = new float3(at.x, homeHeights[h] + 6f, at.y), Smoke = sign.Smoke, Lamp = sign.Lamp });
            }
            if (UnityEngine.Time.time >= homeLogNext)
            {
                homeLogNext = UnityEngine.Time.time + 5f;
                Debug.Log($"[homesign] day {day} hour {hour:F2}: {near} houses within 1.5 km, {smoking} with smoke, {lit} with a lamp");
            }
            var parts = 0;
            foreach (var mark in homeMarks)
                parts += (mark.Smoke > 0.02f ? 1 : 0) + (mark.Lamp > 0.02f ? 1 : 0);
            return parts;
        }

        /// <summary>Writes the gathered markers.</summary>
        int WriteHomeSigns(NativeArray<SegmentInstance> output, int at)
        {
            var up = new float3(0f, 1f, 0f);
            var upright = quaternion.LookRotationSafe(up, new float3(0f, 0f, 1f));
            foreach (var mark in homeMarks)
            {
                if (mark.Lamp > 0.02f)
                    output[at++] = Part(mark.At, upright, 0.1f, 0.7f, 0.7f, 1f, new float4(1f, 0.8f, 0.3f, mark.Lamp));
                if (mark.Smoke > 0.02f)
                    output[at++] = Part(mark.At + up * 1.2f, upright, 0.5f + 5f * mark.Smoke, 0.35f, 0.6f, 1f, new float4(0.75f, 0.75f, 0.78f, 0f));
            }
            return at;
        }
    }
}
