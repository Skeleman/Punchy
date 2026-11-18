using System.IO;
using Punchy.Fauna;
using Punchy.Net;
using Punchy.Player;
using Punchy.Settings;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Debugging
{
    /// <summary>
    /// "-ridesize [Mount] [Rider]" (A-119, #339): a rider and its mount (a person on a horse by default) ride away from
    /// the player in a straight line, from 20 m out to past where they are let go, at a trot. Alone or hosting. Every
    /// 10 m it logs, for each of the two, the detail it was drawn with last frame (geometry, pose rate, shadow, or
    /// undrawn), its think range and whether the mount thinks every frame there, how far it is sent to a player and
    /// let go, and whether it is still in the sim; and the same answers with the pair taken away (Pair cleared: the code
    /// as it was before A-119, each body by its own size and position, the rider always drawn in full). Over the whole
    /// ride it counts the frames where the two were drawn differently (now and before), where the rider was undrawn
    /// or posed slower than its moving mount (frozen), and positions that were not a number. PASS when, now, the two
    /// are never drawn differently, never frozen, share every range and leave the sim in the same frame, with NaN 0.
    /// "-ridesizeshot dir" saves a shot from the player as the pair passes 60, 140 and 200 m. Made on its own when
    /// the flag is there.
    /// </summary>
    public class RideSizeTest : MonoBehaviour
    {
        string mountName = "Horse", riderName = "Human", shotDir;
        Entity mount, rider;
        float3 player;
        float2 heading;
        float startAt = -1f;
        bool done;
        /// <summary>The before answers' hysteresis, kept as the renderer keeps its own.</summary>
        DetailPolicy.Decision beforeMount = DetailPolicy.Decision.First, beforeRider = DetailPolicy.Decision.First;
        int frames, differNow, differBefore, frozenNow, frozenBefore, nan, rangesDiffer;
        int mountGoneFrame = -1, riderGoneFrame = -1;
        float nextLog = From, goneAt = -1f;
        int nextShot;
        static readonly float[] ShotAt = { 60f, 140f, 200f };
        /// <summary>Where the ride starts and how fast it goes (m from the player, m/s: a trot).</summary>
        const float From = 20f, Trot = 6f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Make()
        {
            var args = DebugControls.LaunchArgs;
            var at = System.Array.IndexOf(args, "-ridesize");
            if (at < 0)
                return;
            var go = new GameObject("RideSizeTest");
            DontDestroyOnLoad(go);
            var test = go.AddComponent<RideSizeTest>();
            if (at + 1 < args.Length && !args[at + 1].StartsWith("-"))
                test.mountName = args[at + 1];
            if (at + 2 < args.Length && !args[at + 1].StartsWith("-") && !args[at + 2].StartsWith("-"))
                test.riderName = args[at + 2];
            var shot = System.Array.IndexOf(args, "-ridesizeshot");
            if (shot >= 0 && shot + 1 < args.Length)
                test.shotDir = args[shot + 1];
        }

        void LateUpdate()
        {
            if (done || Time.timeSinceLevelLoad < 12f)
                return;
            var world = World.DefaultGameObjectInjectionWorld;
            var director = FindAnyObjectByType<FaunaDirector>();
            if (world == null || !world.IsCreated || director == null || !director.CanSpawn)
                return;
            var em = world.EntityManager;
            using var players = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
            using var terrains = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            if (!players.TryGetSingleton<LocalTransform>(out var me) || !terrains.TryGetSingleton<TerrainSource>(out var source))
                return;
            var sem = director.SimWorld.EntityManager;
            if (startAt < 0f)
            {
                Begin(director, sem, source, me.Position);
                return;
            }
            Ride(director, sem, source);
        }

        /// <summary>Picks the driest straight line out from the player, puts the pair down at From on it and mounts.</summary>
        void Begin(FaunaDirector director, EntityManager sem, in TerrainSource source, float3 at)
        {
            var mountIndex = director.SpeciesIndex(mountName);
            var riderIndex = director.SpeciesIndex(riderName);
            if (mountIndex < 0 || riderIndex < 0)
            {
                Finish($"no species '{mountName}' or '{riderName}'", false);
                return;
            }
            player = at;
            var best = -1;
            for (int k = 0; k < 24; k++)
            {
                var dir = new float2(math.sin(k * math.PI / 12f), math.cos(k * math.PI / 12f));
                var dry = 0;
                for (var d = From; d <= 260f; d += 10f)
                    dry += source.SampleGround(at.xz + dir * d).HasWater ? 0 : 1;
                if (dry > best)
                {
                    best = dry;
                    heading = dir;
                }
            }
            var start = at.xz + heading * From;
            var yaw = math.atan2(heading.x, heading.y);
            mount = director.Spawn(mountIndex, new float3(start.x, source.SampleHeight(start), start.y), yaw);
            var side = start + new float2(heading.y, -heading.x) * 1.2f;
            rider = director.Spawn(riderIndex, new float3(side.x, source.SampleHeight(side), side.y), yaw);
            foreach (var e in new[] { mount, rider })
            {
                var c = sem.GetComponentData<CreatureState>(e);
                c.Mode = CreatureMode.Puppet;
                c.Speed = c.GaitSpeed = 0f;
                sem.SetComponentData(e, c);
            }
            var refusal = Riding.Mount(sem, rider, mount, source);
            if (refusal != RideRefusal.None)
            {
                Finish($"{riderName} refused by {mountName} ({refusal})", false);
                return;
            }
            // (Then let the mount go its own way, as a ridden animal does, so neither is drawn in full as a puppet's
            // pair is: the ride below only keeps setting where it is, its heading and its pace.)
            var m = sem.GetComponentData<CreatureState>(mount);
            m.Mode = CreatureMode.Wander;
            sem.SetComponentData(mount, m);
            // (The camera at the player's eye, looking down the line, held there for the whole ride.)
            var eye = at + new float3(0f, 2.2f, 0f) - new float3(heading.x, 0f, heading.y) * 2f;
            var look = at.xz + heading * 120f;
            var target = new float3(look.x, source.SampleHeight(look) + 1.5f, look.y);
            DebugControls.RunOptions(System.FormattableString.Invariant($"-camlook {eye.x:F2},{eye.y:F2},{eye.z:F2},{target.x:F2},{target.y:F2},{target.z:F2}"));
            startAt = Time.time;
            Debug.Log($"RideSize: {riderName} on {mountName} from {From} m, {Trot} m/s along ({heading.x:F2}, {heading.y:F2}) " +
                      $"({best} of 25 samples dry), player at ({at.x:F0}, {at.z:F0}), despawn {director.DespawnDistance} m");
        }

        void Ride(FaunaDirector director, EntityManager sem, in TerrainSource source)
        {
            var mountThere = sem.Exists(mount);
            var riderThere = sem.Exists(rider);
            if (!mountThere && mountGoneFrame < 0)
                mountGoneFrame = Time.frameCount;
            if (!riderThere && riderGoneFrame < 0)
                riderGoneFrame = Time.frameCount;
            if (!mountThere || !riderThere)
            {
                if (goneAt < 0f)
                    goneAt = Time.time;
                if (Time.time - goneAt > 2f || mountThere != riderThere && Time.time - goneAt > 1f)
                    Verdict();
                return;
            }
            // The mount moved on along the line (the brain may turn or stop it between: put back on it every frame).
            var d = From + (Time.time - startAt) * Trot;
            var m = sem.GetComponentData<CreatureState>(mount);
            var xz = player.xz + heading * d;
            m.Position = new float3(xz.x, CreatureMotion.Ground(source, xz, m.Position.y), xz.y);
            m.Yaw = math.atan2(heading.x, heading.y);
            m.Speed = m.GaitSpeed = Trot;
            sem.SetComponentData(mount, m);
            if (d > 400f)
            {
                Finish("still not let go at 400 m", false);
                return;
            }

            var r = sem.GetComponentData<CreatureState>(rider);
            var mBody = sem.GetComponentData<CreatureBody>(mount);
            var rBody = sem.GetComponentData<CreatureBody>(rider);
            var renderer = director.SimWorld.GetExistingSystemManaged<CreatureRenderSystem>();
            var view = DetailPolicy.Current.Data;
            if (!math.all(math.isfinite(m.Position)) || !math.all(math.isfinite(r.Position)))
                nan++;
            // Now: as drawn last frame. Before: the same Decide with the pair taken away (as the code was).
            var drawnM = DetailPolicy.Decision.First;
            var drawnR = DetailPolicy.Decision.First;
            var seenM = renderer != null && renderer.LastDrawn(mount, out drawnM);
            var seenR = renderer != null && renderer.LastDrawn(rider, out drawnR);
            var mOld = m;
            mOld.Pair = default;
            var rOld = r;
            rOld.Pair = default;
            beforeMount = CreatureRenderSystem.DecideFor(mOld, mBody, view, beforeMount);
            beforeRider = CreatureRenderSystem.DecideFor(rOld, rBody, view, beforeRider);
            // (Paired from the second frame on: the first frame's seating only fills PairNext.)
            if (r.Pair.Size > 0f)
            {
                frames++;
                if (seenM != seenR || seenM && Tiers(drawnM) != Tiers(drawnR))
                    differNow++;
                if (Tiers(beforeMount) != Tiers(beforeRider))
                    differBefore++;
                if (seenM && (!seenR || drawnR.Animation > drawnM.Animation))
                    frozenNow++;
                if (beforeRider.Animation > beforeMount.Animation)
                    frozenBefore++;
                if (CreatureBrainSystem.ThinkRange(m, ref mBody.Blob.Value) != CreatureBrainSystem.ThinkRange(r, ref rBody.Blob.Value) ||
                    director.LetGoRange(m, ref mBody.Blob.Value) != director.LetGoRange(r, ref rBody.Blob.Value) ||
                    !CreatureMotion.SeenAt(m).Equals(CreatureMotion.SeenAt(r)))
                    rangesDiffer++;
            }
            var dist = math.distance(player.xz, m.Position.xz);
            if (dist >= nextLog)
            {
                nextLog += 10f;
                var cam = Camera.main;
                var screen = cam != null ? cam.WorldToScreenPoint(m.Position + new float3(0f, 1.2f, 0f)) : Vector3.zero;
                Debug.Log(System.FormattableString.Invariant(
                    $"RideSize {dist:F0} m: now {mountName} {Show(seenM, drawnM)} {riderName} {Show(seenR, drawnR)}, ") +
                    Ranges("now", director, m, mBody, r, rBody) +
                    System.FormattableString.Invariant($"; before {mountName} {Show(true, beforeMount)} {riderName} {Show(true, beforeRider)}, ") +
                    Ranges("before", director, mOld, mBody, rOld, rBody) +
                    System.FormattableString.Invariant($"; pair size {r.Pair.Size:F2} m (own {CreatureMotion.OwnSize(m.Scale, ref mBody.Blob.Value):F2} / {CreatureMotion.OwnSize(r.Scale, ref rBody.Blob.Value):F2}); on screen ({screen.x:F0}, {screen.y:F0}) of {Screen.width}x{Screen.height}"));
            }
            if (shotDir != null && nextShot < ShotAt.Length && dist >= ShotAt[nextShot])
            {
                Directory.CreateDirectory(shotDir);
                var path = Path.Combine(shotDir, $"a119-ride-{ShotAt[nextShot]:F0}m.png");
                ScreenCapture.CaptureScreenshot(path);
                Debug.Log($"RideSize: shot {path} at {dist:F0} m");
                nextShot++;
            }
        }

        /// <summary>The think, send and let-go ranges of each, and whether the mount thinks every frame where it is.</summary>
        static string Ranges(string label, FaunaDirector director, in CreatureState m, CreatureBody mBody, in CreatureState r, CreatureBody rBody)
        {
            var thinkM = CreatureBrainSystem.ThinkRange(m, ref mBody.Blob.Value);
            var thinkR = CreatureBrainSystem.ThinkRange(r, ref rBody.Blob.Value);
            var near = math.distance(CreatureMotion.SeenAt(m).xz, CreatureMotion.SeenAt(r).xz);
            return System.FormattableString.Invariant(
                $"think {thinkM:F0}/{thinkR:F0} m, send {CreatureRelevancySystem.SendRadius(m, ref mBody.Blob.Value, 0):F0}/{CreatureRelevancySystem.SendRadius(r, ref rBody.Blob.Value, 0):F0} m, ") +
                System.FormattableString.Invariant($"let go {director.LetGoRange(m, ref mBody.Blob.Value):F0}/{director.LetGoRange(r, ref rBody.Blob.Value):F0} m, measured {near:F2} m apart");
        }

        /// <summary>Geometry, pose rate and shadow: what the two have to share (fur and feet go by each one's own size).</summary>
        static int Tiers(DetailPolicy.Decision d) => d.Packed & 31;

        static string Show(bool drawn, DetailPolicy.Decision d)
            => drawn ? $"g{d.Geometry} pose 1/{1 << d.Animation}{(d.Shadow ? " shadow" : "")}" : "undrawn";

        void Verdict()
        {
            var together = mountGoneFrame >= 0 && mountGoneFrame == riderGoneFrame;
            var pass = differNow == 0 && frozenNow == 0 && rangesDiffer == 0 && nan == 0 && together && frames > 0;
            Finish($"over {frames} paired frames: drawn differently now {differNow} (before {differBefore}), rider frozen or " +
                   $"undrawn on its moving mount now {frozenNow} (before {frozenBefore}), ranges or position differ {rangesDiffer}, " +
                   $"NaN {nan}; let go in frame {mountGoneFrame} ({mountName}) and {riderGoneFrame} ({riderName}) " +
                   $"({(together ? "together" : "APART")})", pass);
        }

        void Finish(string why, bool ok)
        {
            done = true;
            Debug.Log($"RideSize: {why} => {(ok ? "PASS" : "FAIL")}");
        }
    }
}
