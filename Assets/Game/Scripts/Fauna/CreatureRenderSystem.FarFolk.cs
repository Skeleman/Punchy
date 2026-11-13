using System.Collections.Generic;
using Punchy.Settings;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// The towns' far figures (A-109 row 2), drawn only: each household member FarFolk places is drawn as a walker (a
    /// coarse body of six capsules, legs and arms swinging as it goes) while it looks FarFolkWalkerPixels tall or more,
    /// and as a mote (one capsule) below that. Under FarFolkMotePixels a mote is held at that size and thinned by dither
    /// until it is gone at half of it, so a town a kilometre off still has a few specks moving in it. Where the town's
    /// real folk are in the world, the far figures give way to them over the same range the real ones fade in.
    /// </summary>
    public partial class CreatureRenderSystem
    {
        /// <summary>How tall (reference pixels, DetailPolicy.ScreenSize) a far figure has to look to be drawn as a
        /// walker, and the size a mote is held at when it would look smaller. (R-24 row 4 moves these to DetailPolicy.)</summary>
        const float FarFolkWalkerPixels = 6f, FarFolkMotePixels = 2f;
        /// <summary>Where creatures fade out with distance (PoseJob.FadeFar and FadeOutRange): the far figures of a town
        /// whose real folk are about fade in over the same stretch, so the two change places.</summary>
        const float CreatureFadeFar = 165f, CreatureFadeRange = 25f;
        const int FarWalkerParts = 6;

        struct FarShown
        {
            public float3 Position;
            public float2 Facing;
            public float Walked, Scale, Faded, Walker, Pixels, Work;
            public bool Walking;
            public float4 Body, Limbs;
        }

        readonly List<FarShown> farShown = new List<FarShown>();
        bool farLog, farLogRead;
        float farLogNext;

        bool FarLog
        {
            get
            {
                if (!farLogRead)
                {
                    farLogRead = true;
                    farLog = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-farfolk") >= 0;
                }
                return farLog;
            }
        }

        /// <summary>
        /// The far figures in view now (t: seconds on the shared clock; hour: the time of day). Returns how many
        /// capsules they need.
        /// </summary>
        int GatherFarFolk(float3 cam, double t, float hour)
        {
            farShown.Clear();
            if (CaveSystem.SurfaceHidden >= 0.99f || FarFolk.Towns.Count == 0 || !Households.Ready)
                return 0;
            var pixelsPerMetre = DetailPolicy.Current.Data.PixelsPerMetre;
            var dt = SystemAPI.Time.DeltaTime;
            var frame = UnityEngine.Time.frameCount;
            var perHour = skyCycle != null ? skyCycle.DayLengthMinutes * 2.5f : 60f;
            var parts = 0;
            int walkers = 0, motes = 0, drawnTowns = 0;
            foreach (var town in FarFolk.Towns)
            {
                // (Its real folk fade in over 1.2 s, PoseJob.FadeInTime: the far figures near them leave as slowly.)
                town.RealBlend = math.saturate(town.RealBlend + (town.Real ? dt : -dt) / 1.2f);
                var nearest = math.max(math.distance(cam, town.Centre) - town.Radius, 1f);
                town.Drawn = DetailPolicy.Holds(DetailPolicy.ScreenSize(FarFolk.FigureHeight, nearest, pixelsPerMetre),
                                                FarFolkMotePixels * 0.5f, town.Drawn);
                if (!town.Drawn)
                    continue;
                drawnTowns++;
                foreach (var f in town.Figures)
                {
                    if (f.House >= 0 && f.Member >= 0 && (f.House >= Households.All.Count || Households.All[f.House].Gone(f.Member)))
                        continue;
                    if (!FarFolk.Place(f, hour, t, perHour, out var position, out var facing, out var walked, out var shown, out var walking))
                        continue;
                    var distance = math.distance(cam, position);
                    var pixels = DetailPolicy.ScreenSize(FarFolk.FigureHeight * f.Scale, distance, pixelsPerMetre);
                    // Near the camera a peopled town's real folk are drawn (they fade out from 140 m to 165 m): the far
                    // figures fade in over that stretch. A town with no real folk here keeps its far figures throughout.
                    var near = math.lerp(1f, math.saturate((distance - (CreatureFadeFar - CreatureFadeRange)) / CreatureFadeRange), town.RealBlend);
                    // (Ruling 4: under the mote size, thinned by dither, gone at half of it.)
                    var thin = math.saturate(pixels / (FarFolkMotePixels * 0.5f) - 1f);
                    // (The size cap: too big to pass for a person, it fades out over half a second, and comes back
                    // once it is Hysteresis smaller. Straight to it when it wasn't weighed the frame before, or when it
                    // is far over the cap, which no walk does in half a second: the camera jumped into the town.)
                    f.Capped = DetailPolicy.Holds(pixels, DetailPolicy.FarFigureMaxPixels, f.Capped);
                    var capNow = f.CapFrame != frame - 1 || pixels > DetailPolicy.FarFigureMaxPixels * 1.5f;
                    f.CapBlend = capNow ? f.Capped ? 1f : 0f : math.saturate(f.CapBlend + (f.Capped ? dt : -dt) * 2f);
                    f.CapFrame = frame;
                    var faded = 1f - shown * near * thin * (1f - f.CapBlend);
                    if (faded >= 0.99f)
                        continue;
                    f.Walker = DetailPolicy.Holds(pixels, FarFolkWalkerPixels, f.Walker);
                    var target = f.Walker ? 1f : 0f;
                    // (Half a second between walker and mote; straight to it when it wasn't in view the frame before.)
                    f.WalkerBlend = f.SeenFrame == frame - 1 ? math.saturate(f.WalkerBlend + (f.Walker ? dt : -dt) * 2f) : target;
                    f.SeenFrame = frame;
                    if (!f.Capped) // (One over the cap is on its half-second way out.)
                    {
                        town.MaxPixels = math.max(town.MaxPixels, pixels);
                        town.NowPixels = math.max(town.NowPixels, pixels);
                    }
                    farShown.Add(new FarShown
                    {
                        Position = position,
                        Facing = facing,
                        Walked = walked,
                        Walking = walking,
                        Scale = f.Scale,
                        Faded = faded,
                        Walker = f.WalkerBlend,
                        Pixels = pixels,
                        Work = f.Posted && !walking ? 1f + FarFolk.Hash01(f.Seed, 21) * 6f : 0f,
                        Body = f.Body,
                        Limbs = f.Limbs
                    });
                    if (f.WalkerBlend > 0f)
                    {
                        parts += FarWalkerParts;
                        walkers++;
                    }
                    if (f.WalkerBlend < 1f)
                    {
                        parts++;
                        motes++;
                    }
                }
            }
            if (FarLog && UnityEngine.Time.time >= farLogNext)
            {
                farLogNext = UnityEngine.Time.time + 5f;
                var line = new System.Text.StringBuilder();
                foreach (var town in FarFolk.Towns)
                    if (town.Drawn)
                    {
                        line.Append($" [{town.Name} #{town.Landmark} ({town.Centre.x:F0},{town.Centre.z:F0}) r {town.Radius:F0} at {math.distance(cam, town.Centre):F0} m: " +
                                    $"{town.Figures.Count} figures, real {(town.Real ? "yes" : "no")}, largest {town.MaxPixels:F1} px, {town.NowPixels:F1} px since the last line]");
                        town.NowPixels = 0f;
                    }
                Debug.Log($"[farfolk] hour {hour:F2}: {FarFolk.Towns.Count} towns, {drawnTowns} in view, {walkers} walkers, {motes} motes, {parts} capsules;{line}");
            }
            return parts;
        }

        /// <summary>Writes the gathered far figures.</summary>
        int WriteFarFolk(double t, NativeArray<SegmentInstance> output, int at)
        {
            var up = new float3(0f, 1f, 0f);
            var time = (float)(t % 10000.0);
            var metresPerPixel = 1f / math.max(DetailPolicy.Current.Data.PixelsPerMetre, 1f);
            var camera = DetailPolicy.Current.Data.Camera;
            foreach (var f in farShown)
            {
                var forward = new float3(f.Facing.x, 0f, f.Facing.y);
                var right = math.cross(up, forward);
                var s = f.Scale;
                if (f.Walker > 0f)
                {
                    var faded = math.max(f.Faded, 1f - f.Walker);
                    // Legs and arms swing with the ground covered (a 1.4 m stride); at work the arms go up and down.
                    var swing = f.Walking ? math.sin(f.Walked * 2f * math.PI / (1.4f * s)) * 0.55f : 0f;
                    var arms = f.Work > 0f ? 0.7f + 0.5f * math.sin(time * 3.1f + f.Work) : -swing * 0.8f;
                    var upright = quaternion.LookRotationSafe(up, forward);
                    output[at++] = Part(f.Position + up * 0.84f * s, upright, 0.56f * s, 0.17f * s, 0.19f * s, 1f, f.Body, faded);
                    output[at++] = Part(f.Position + up * 1.52f * s, upright, 0.06f * s, 0.115f * s, 0.115f * s, 1f, f.Limbs, faded);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var a = swing * side;
                        var leg = math.normalize(-up * math.cos(a) + forward * math.sin(a));
                        output[at++] = Part(f.Position + up * 0.86f * s + right * side * 0.09f * s, quaternion.LookRotationSafe(leg, forward),
                                            0.8f * s, 0.075f * s, 0.06f * s, 1f, f.Limbs, faded);
                        var b = f.Work > 0f ? arms : arms * side;
                        var arm = math.normalize(-up * math.cos(b) + forward * math.sin(b));
                        output[at++] = Part(f.Position + up * 1.36f * s + right * side * 0.24f * s, quaternion.LookRotationSafe(arm, forward),
                                            0.55f * s, 0.055f * s, 0.045f * s, 1f, f.Body, faded);
                    }
                }
                if (f.Walker < 1f)
                {
                    // A mote: one upright capsule, never under the mote size tall nor under that wide (a figure's true
                    // width is under a pixel long before its height is, and a sliver that thin is lost against the ground).
                    var distance = math.distance(camera, f.Position);
                    var radius = math.max(0.2f * s, FarFolkMotePixels * 0.5f * distance * metresPerPixel);
                    var height = FarFolk.FigureHeight * s * math.max(1f, FarFolkMotePixels / math.max(f.Pixels, 0.01f));
                    output[at++] = Part(f.Position + up * radius, quaternion.LookRotationSafe(up, forward),
                                        math.max(height - 2f * radius, 0.05f), radius, radius * 0.8f, 1f, f.Body, math.max(f.Faded, f.Walker));
                }
            }
            return at;
        }
    }
}
