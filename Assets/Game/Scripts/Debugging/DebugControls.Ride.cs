using System.Collections.Generic;
using Punchy.Fauna;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Debugging
{
    public partial class DebugControls
    {
        /// <summary>"-ridetest" (A-104 row 1): three rider/mount pairs, one refused, the mounts walked for a minute.</summary>
        bool rideTest;
        bool rideDone;
        /// <summary>When the pairs went down (Time.time); -1 before.</summary>
        float rideAt = -1f;
        /// <summary>Each pair's mount and rider (the rider Entity.Null once refused), its names, and where it walks round.</summary>
        readonly List<Entity> rideMounts = new List<Entity>();
        readonly List<Entity> rideRiders = new List<Entity>();
        readonly List<string> rideNames = new List<string>();
        readonly List<float3> rideCentres = new List<float3>();
        /// <summary>Per pair: the largest seat offset seen in the sim world and as drawn on this machine, and how many
        /// frames the drawn one was measured in; and the last drawn offset (-1 when none).</summary>
        readonly List<float3> rideMarks = new List<float3>();
        readonly List<float> rideSeen = new List<float>();
        /// <summary>Per pair: the largest drawn offset in the first second, while the riding ghosts arrive (a client's
        /// re-seat waits for CreatureSnapshotSystem's next copy to count them, as a held animal's does: one frame).</summary>
        readonly List<float> rideFirst = new List<float>();
        bool rideWatching;
        /// <summary>How long the mounts walk (s), and how fast (m/s) round how wide a circle (m).</summary>
        const float RideTestTime = 60f, RideWalk = 1.1f, RideCircle = 3f;

        /// <summary>
        /// "-ridetest": once the world has settled, on dry ground near the player (solo or hosting), puts down three
        /// pairs 9 m apart as puppets: a cat to ride a dog, a child to ride an adult, and a dog to ride a cat (refused:
        /// too heavy, and the refusal logged). Logs each pair's weights, body plans and how the rider sits. Then each
        /// mount walks round a 3 m circle for RideTestTime while the riders ride; each second logs, per pair, how far the
        /// rider's contact is from its seat in the sim world and as this machine draws it (the client's ghosts, re-seated
        /// by RideSeatSystem, measured at the end of every frame once everything has run), and at the end the largest of
        /// each (the drawn one after the first second, see rideFirst): a rider that lagged or jittered would leave its
        /// seat in some frame. PASS when both riders stayed within 1 cm of their seats and the
        /// dog was refused. The mounts then stand still facing +x, and a -camlook line per pair is logged for its shot.
        /// </summary>
        void RideTest()
        {
            if (!rideTest || rideDone || Time.timeSinceLevelLoad - optionsAt < settle || !EnsureLocations(out var source) ||
                !playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            var director = FindAnyObjectByType<FaunaDirector>();
            if (director == null || director.SimWorld == null || !director.SimWorld.IsCreated)
                return;
            if (!director.CanSpawn)
            {
                Debug.LogWarning("RideTest: needs a solo or hosting game");
                rideDone = true;
                return;
            }
            var em = director.SimWorld.EntityManager;
            if (rideAt < 0f)
            {
                // Three dry, level spots 9 m apart in a row, nearest the player (each with room to walk round), on rings out to 80 m.
                var found = false;
                var at = float2.zero;
                for (var r = 6f; r <= 80f && !found; r += 4f)
                    for (int k = 0; k < 24 && !found; k++)
                    {
                        var a = k * math.PI * 2f / 24f;
                        var xz = player.Position.xz + new float2(math.sin(a), math.cos(a)) * r;
                        var dry = true;
                        for (int p = -1; p <= 1 && dry; p++)
                            for (int q = 0; q < 8 && dry; q++)
                            {
                                var b = q * math.PI * 0.25f;
                                var g = source.SampleGround(xz + new float2(p * 9f + math.sin(b) * (RideCircle + 1f), math.cos(b) * (RideCircle + 1f)));
                                // (Dry, and nearly level round each circle: within 1 m of its middle.)
                                dry = !g.HasWater && math.abs(g.Height - source.SampleHeight(xz + new float2(p * 9f, 0f))) < 1f;
                            }
                        if (dry)
                        {
                            found = true;
                            at = xz;
                        }
                    }
                if (!found)
                {
                    Debug.LogWarning($"RideTest: no dry ground for three pairs within 80 m of ({player.Position.x:F0}, {player.Position.z:F0})");
                    rideDone = true;
                    return;
                }
                var pairs = new[] { ("Dog", "Cat"), ("Human", "HumanChild"), ("Cat", "Dog") };
                for (int i = 0; i < pairs.Length; i++)
                {
                    var centre = at + new float2((i - 1) * 9f, 0f);
                    var mountIndex = director.SpeciesIndex(pairs[i].Item1);
                    var riderIndex = director.SpeciesIndex(pairs[i].Item2);
                    if (mountIndex < 0 || riderIndex < 0)
                    {
                        Debug.LogWarning($"RideTest: no species '{pairs[i].Item1}' or '{pairs[i].Item2}'");
                        continue;
                    }
                    // (The mount on its circle, facing along it; the rider beside it till it gets on.)
                    var start = centre + new float2(RideCircle, 0f);
                    var mount = director.Spawn(mountIndex, new float3(start.x, source.SampleHeight(start), start.y), 0f);
                    var side = start + new float2(1.2f, 0f);
                    var rider = director.Spawn(riderIndex, new float3(side.x, source.SampleHeight(side), side.y), 0f);
                    foreach (var e in new[] { mount, rider })
                    {
                        var c = em.GetComponentData<CreatureState>(e);
                        c.Mode = CreatureMode.Puppet;
                        c.Speed = c.GaitSpeed = 0f;
                        c.Yaw = 0f;
                        em.SetComponentData(e, c);
                    }
                    var m = em.GetComponentData<CreatureState>(mount);
                    var rs = em.GetComponentData<CreatureState>(rider);
                    ref var mb = ref em.GetComponentData<CreatureBody>(mount).Blob.Value;
                    ref var rb = ref em.GetComponentData<CreatureBody>(rider).Blob.Value;
                    var pose = Riding.PoseFor(ref rb, rs, ref mb, m);
                    var refusal = Riding.Mount(em, rider, mount, source);
                    var name = $"{pairs[i].Item2} on {pairs[i].Item1}";
                    Debug.Log($"RideTest: {name}: rider {rb.WeightAt(rs.Scale):F1} kg x {Riding.MassRatio} = {rb.WeightAt(rs.Scale) * Riding.MassRatio:F1} kg " +
                              $"(scale {rs.Scale:F2}, upright {rb.Upright}, child {rb.Child}) vs mount {mb.WeightAt(m.Scale):F1} kg " +
                              $"(scale {m.Scale:F2}, upright {mb.Upright}): " +
                              (refusal == RideRefusal.None ? $"mounted, pose {pose}" : $"REFUSED ({refusal})"));
                    rideMounts.Add(mount);
                    rideRiders.Add(refusal == RideRefusal.None ? rider : Entity.Null);
                    rideNames.Add(name);
                    rideCentres.Add(new float3(centre.x, source.SampleHeight(centre), centre.y));
                    rideMarks.Add(float3.zero);
                    rideSeen.Add(-1f);
                    rideFirst.Add(0f);
                }
                rideAt = Time.time;
                if (!rideWatching)
                {
                    rideWatching = true;
                    StartCoroutine(RideWatch());
                }
                return;
            }

            var since = Time.time - rideAt;
            var walking = since < RideTestTime;
            var tick = Mathf.FloorToInt(since) != Mathf.FloorToInt(since - Time.deltaTime);
            var line = tick && walking ? new System.Text.StringBuilder($"RideTest {since:F0} s:") : null;
            for (int i = 0; i < rideMounts.Count; i++)
            {
                var mount = rideMounts[i];
                if (!em.Exists(mount))
                    continue;
                var m = em.GetComponentData<CreatureState>(mount);
                var rider = rideRiders[i];
                if (rider != Entity.Null && em.Exists(rider))
                {
                    // How the rider sits now, as the sim world last placed it (after the mount moved).
                    var r = em.GetComponentData<CreatureState>(rider);
                    var rBody = em.GetComponentData<CreatureBody>(rider);
                    var mBody = em.GetComponentData<CreatureBody>(mount);
                    var offset = Riding.Offset(ref rBody.Blob.Value, r, rBody.Coat, ref mBody.Blob.Value, m, mBody.Coat, source);
                    var mark = rideMarks[i];
                    var seen = rideSeen[i];
                    if (walking)
                    {
                        mark.x = math.max(mark.x, offset);
                        rideMarks[i] = mark;
                    }
                    line?.Append($" {rideNames[i]}: mount ({m.Position.x:F1}, {m.Position.y:F2}, {m.Position.z:F1}) yaw {math.degrees(m.Yaw):F0} " +
                                 $"{r.Mode}/{(RidePose)r.Stage} offset {offset * 100f:F2} cm drawn {(seen < 0f ? "-" : $"{seen * 100f:F2} cm")};");
                }
                // The mount walks round its circle (anticlockwise seen from above), then stands facing +x; a refused one
                // stands by the rider it refused throughout (both in its shot).
                if (walking && rider != Entity.Null)
                {
                    var c = rideCentres[i];
                    var angle = math.atan2(m.Position.x - c.x, m.Position.z - c.z);
                    m.Yaw = angle - math.PI * 0.5f;
                    // (Steered back onto the circle if it drifts off it: inward is the lower yaw on this heading.)
                    m.Yaw -= math.clamp((math.distance(m.Position.xz, c.xz) - RideCircle) * 0.3f, -0.3f, 0.3f);
                    m.Speed = m.GaitSpeed = RideWalk;
                }
                else
                {
                    m.Yaw = math.PI * 0.5f;
                    m.Speed = m.GaitSpeed = 0f;
                }
                em.SetComponentData(mount, m);
            }
            if (line != null)
                Debug.Log(line.ToString());
            // Two seconds after the walk, the verdict and where to stand for each shot (the mounts standing still).
            if (since < RideTestTime + 2f)
                return;
            rideDone = true;
            rideWatching = false;
            var pass = rideMounts.Count == 3;
            var summary = new System.Text.StringBuilder();
            for (int i = 0; i < rideMounts.Count; i++)
            {
                var refused = rideRiders[i] == Entity.Null;
                var mark = rideMarks[i];
                bool ok;
                if (i == 2)
                {
                    ok = refused;
                    summary.Append($" {rideNames[i]}: {(refused ? "refused" : "NOT refused")} ({(ok ? "ok" : "WRONG")});");
                }
                else
                {
                    // (Drawn: only where there are ghosts, on a host; solo the sim world is the one drawn.)
                    var drawnOk = queryWorld == director.SimWorld || mark.z > 0f && mark.y < 0.01f;
                    ok = !refused && mark.x < 0.01f && drawnOk;
                    summary.Append($" {rideNames[i]}: max offset {mark.x * 100f:F2} cm (sim), {mark.y * 100f:F2} cm (drawn, {mark.z:F0} frames; " +
                                   $"{rideFirst[i] * 100f:F2} cm in the first second, as the ghosts arrive) ({(ok ? "ok" : "WRONG")});");
                }
                pass &= ok;
                if (em.Exists(rideMounts[i]))
                {
                    var m = em.GetComponentData<CreatureState>(rideMounts[i]);
                    // (From its right-hand side, a little ahead and above, looking at its back.)
                    var target = m.Position + new float3(0f, i == 1 ? 1.1f : 0.45f, 0f);
                    var eye = target + new float3(0.6f, i == 1 ? 0.4f : 0.5f, -2.6f) * (i == 1 ? 1.4f : 1f);
                    Debug.Log($"RideTest: shot {rideNames[i]}: -camlook {eye.x:F2},{eye.y:F2},{eye.z:F2},{target.x:F2},{target.y:F2},{target.z:F2}");
                }
            }
            Debug.Log($"RideTest: {(pass ? "PASS" : "FAIL")}:{summary} over {RideTestTime:F0} s walking {RideWalk} m/s round {RideCircle} m circles");
        }

        /// <summary>At the end of each frame of the walk (after presentation, as it is drawn), each pair's drawn seat offset.</summary>
        System.Collections.IEnumerator RideWatch()
        {
            var end = new WaitForEndOfFrame();
            while (rideWatching)
            {
                yield return end;
                var since = Time.time - rideAt;
                var walking = since < RideTestTime;
                var drawn = DrawnOffsets();
                for (int i = 0; i < rideNames.Count; i++)
                {
                    var seen = drawn.TryGetValue(rideNames[i], out var d) ? d : -1f;
                    rideSeen[i] = seen;
                    if (since < 1f && seen >= 0f)
                        rideFirst[i] = math.max(rideFirst[i], seen);
                    else if (walking && seen >= 0f)
                    {
                        var mark = rideMarks[i];
                        mark.y = math.max(mark.y, seen);
                        mark.z += 1f;
                        rideMarks[i] = mark;
                    }
                }
            }
        }

        /// <summary>
        /// How far each rider is from its seat as this machine draws it (the player's world: on a host its client world,
        /// where RideSeatSystem re-seats the riders' ghosts), by pair name; empty when there are no ghosts (solo: the sim
        /// world is the one drawn).
        /// </summary>
        Dictionary<string, float> DrawnOffsets()
        {
            var result = new Dictionary<string, float>();
            var world = queryWorld;
            if (world == null || !world.IsCreated || !EnsureLocations(out var source))
                return result;
            var cem = world.EntityManager;
            using var query = cem.CreateEntityQuery(ComponentType.ReadOnly<GhostInstance>(), ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            if (query.IsEmpty)
                return result;
            using var ghosts = query.ToComponentDataArray<GhostInstance>(Allocator.Temp);
            using var states = query.ToComponentDataArray<CreatureState>(Allocator.Temp);
            using var bodies = query.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var byId = new Dictionary<int, int>();
            for (int i = 0; i < ghosts.Length; i++)
                byId[ghosts[i].ghostId] = i;
            for (int i = 0; i < states.Length; i++)
            {
                var r = states[i];
                if (r.Mode != CreatureMode.Ride || r.Holder >= 0 || r.Holder == Riding.SoloMount || !byId.TryGetValue(-r.Holder, out var mi))
                    continue;
                // (Named as the sim world's pairs are, by the pair whose mount stands nearest.)
                var best = -1;
                var bestD = float.MaxValue;
                for (int p = 0; p < rideCentres.Count; p++)
                {
                    var dd = math.distance(states[mi].Position.xz, rideCentres[p].xz);
                    if (dd < bestD)
                    {
                        bestD = dd;
                        best = p;
                    }
                }
                if (best < 0)
                    continue;
                var rb = bodies[i];
                var mb = bodies[mi];
                result[rideNames[best]] = Riding.Offset(ref rb.Blob.Value, r, rb.Coat, ref mb.Blob.Value, states[mi], mb.Coat, source);
            }
            return result;
        }
    }
}
