using System.Collections.Generic;
using Punchy.Flora;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-103 row 1: the turtle folk of a turtle village. One household lives in each igloo (the overworld table's
    /// TurtleHousehold rows, Count folk per igloo): every one of them goes in and out by its own igloo's door
    /// (CreatureState.OwnDoor, which FindDoor takes before any house anchor), so they go home at dusk and come out by
    /// day. Each household holds the village's mountain jobs between them: an ice fisher out on the snow past the
    /// igloos, an ice cutter beside its igloo, one fire tender conjuring the spirit fire in the village's middle (there
    /// is no hearth prop: the flame is drawn as its gear), and the elders, who sit at their porch. Host only; the folk
    /// are ghosts like any other, so a client sees them (with the job their task implies: see FolkJobs). They draw
    /// from their own rng per village, so no other spawn's draws move, and they add no placement.
    /// </summary>
    public partial class FaunaDirector
    {
        /// <summary>The flora species whose placed props are the turtle folk's homes.</summary>
        const string IglooName = "TurtleIgloo";
        /// <summary>
        /// The least distance from an igloo's middle to its door spot, out along its tunnel (StructureKit.Igloo: the door
        /// stands about 0.6 of the dome's radius in, the tunnel ends 1-1.8 m past its foot); a bigger dome's spot is 0.8
        /// of its radius out, between the door's collider and the tunnel's mouth.
        /// </summary>
        const float IglooDoor = 4.2f;
        /// <summary>How far out from an igloo's middle its porch is (where the elders sit), and its cutter's block.</summary>
        const float IglooPorch = 7.5f;
        const uint TurtleSalt = 0x7E2713u;

        /// <summary>Each igloo's first and second folk's jobs, by the igloo's number round the village.</summary>
        static readonly FolkJob[] FirstJobs = { FolkJob.IceFisher, FolkJob.IceCutter, FolkJob.FireTender, FolkJob.Elder, FolkJob.IceFisher };
        static readonly FolkJob[] SecondJobs = { FolkJob.IceCutter, FolkJob.IceFisher, FolkJob.Elder, FolkJob.IceFisher, FolkJob.IceCutter };

        readonly List<string> turtleLog = new List<string>();

        /// <summary>Peoples a turtle village's igloos (nothing for any other landmark).</summary>
        void SpawnTurtleFolk(in TerrainSource source, LandmarkType type, in LandmarkInstance inst, int index, float radius)
        {
            if (overworld == null || type == null || type.name != TurtleVillageBaking.TypeName || !floraBlob.IsCreated)
                return;
            var rows = TableRows(OverworldSlot.TurtleHousehold, type).ToArray();
            ref var f = ref floraBlob.Value;
            if (rows.Length == 0 || inst.Type * 3 + 2 >= f.PropRanges.Length)
                return;
            var r = new Unity.Mathematics.Random(math.hash(new int2(index, (int)TurtleSalt)) | 1u);
            var centre = inst.Position;
            var igloo = 0;
            var total = 0;
            turtleLog.Clear();
            turtleDoors.Clear();
            turtleIgloos.Clear();
            for (int l = 0; l < 3; l++)
            {
                var placed = f.PropRanges[inst.Type * 3 + l];
                for (int e = placed.x; e < placed.x + placed.y; e++)
                {
                    var prop = f.Props[e];
                    if (prop.Species < 0 || prop.Species >= floraSpecies.Count || floraSpecies[prop.Species] == null ||
                        floraSpecies[prop.Species].name != IglooName)
                        continue;
                    var porch = f.Rules[prop.Species].Porch;
                    for (int k = 0; k < prop.Count; k++, igloo++)
                    {
                        var angle = prop.Angle + k * 360f / math.max(1, prop.Count);
                        var ring = LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(prop.Ring.x, angle), radius);
                        // Where the igloo really stands: FloraPlacement seats it with the same call (no draws), which may
                        // move it a few metres and turn it up to 30 degrees off the centre, or leave it out (no household).
                        var yaw = math.atan2(-(centre - ring).x, (centre - ring).y);
                        if (porch <= 0f || FloraPlacement.IglooSpot(source, ring, centre, porch, ref yaw, out var seated) < 0)
                        {
                            turtleLog.Add($"igloo {igloo} not placed");
                            continue;
                        }
                        var at = seated.xz;
                        var toCentre = new float2(-math.sin(yaw), math.cos(yaw));   // the door's way out (the model's +Z)
                        var side = new float2(toCentre.y, -toCentre.x);
                        var doorAt = at + toCentre * math.max(IglooDoor, (porch - 2.2f) * 0.8f);
                        var door = new float4(doorAt.x, 0f, doorAt.y, math.atan2(toCentre.x, toCentre.y));
                        turtleDoors.Add(door);
                        turtleIgloos.Add(new float3(at, porch));
                        var jobs = new System.Text.StringBuilder();
                        var residents = 0;
                        foreach (var ri in rows)
                        {
                            var row = overworld.Rows[ri];
                            var count = r.NextInt(math.max(0, row.Count.x), math.max(row.Count.x, row.Count.y) + 1);
                            for (int n = 0; n < count; n++)
                            {
                                var job = n == 0 ? FirstJobs[igloo % FirstJobs.Length] : n == 1 ? SecondJobs[igloo % SecondJobs.Length] : FolkJob.Elder;
                                var sideSign = (n & 1) == 0 ? 1f : -1f;
                                if (!TurtlePost(source, inst, radius, at, angle, toCentre, side * sideSign, job, ref r, out var post))
                                    continue;
                                if (SpawnTurtle(source, row, post, door, ref r))
                                {
                                    residents++;
                                    jobs.Append(jobs.Length > 0 ? ", " : "").Append(job);
                                }
                            }
                        }
                        total += residents;
                        turtleLog.Add($"igloo {igloo} residents {residents} ({jobs})");
                    }
                }
            }
            Debug.Log($"[turtlefolk] village {index} at ({centre.x:F0}, {centre.y:F0}): {igloo} igloos, {total} folk; " + string.Join("; ", turtleLog));
            // Row 3: its children, from their own rng.
            SpawnTurtleChildren(source, type, inst, index);
        }

        /// <summary>
        /// Where a turtle folk with this job lives and works: an ice fisher on the snow past the igloos (out by its own
        /// igloo, facing away from the village); an ice cutter at its block beside its igloo, facing the wall it builds;
        /// the fire tender a little off the village's middle, facing it; an elder at its porch, to one side of the door.
        /// False if the spot is under water.
        /// </summary>
        static bool TurtlePost(in TerrainSource source, in LandmarkInstance inst, float radius, float2 igloo, float angle,
                               float2 toCentre, float2 side, FolkJob job, ref Unity.Mathematics.Random r, out Post post)
        {
            post = new Post { Job = job };
            switch (job)
            {
                case FolkJob.IceFisher:
                {
                    var spot = LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(r.NextFloat(0.82f, 0.92f), angle + r.NextFloat(-16f, 16f)), radius);
                    post.Task = CreatureTask.Fish;
                    post.Stand = spot;
                    post.Face = spot + math.normalizesafe(spot - inst.Position) * 2f;
                    break;
                }
                case FolkJob.IceCutter:
                    post.Task = CreatureTask.Chop;
                    post.Stand = igloo + side * IglooPorch + toCentre * 1.5f;
                    post.Face = post.Stand - side;
                    break;
                case FolkJob.FireTender:
                    post.Task = CreatureTask.Tend;
                    post.Stand = inst.Position - toCentre * 2.2f;
                    post.Face = inst.Position;
                    break;
                default:
                    post.Task = CreatureTask.None;
                    post.Stand = igloo + toCentre * IglooPorch + side * r.NextFloat(1.2f, 2.2f);
                    post.Face = post.Stand + toCentre;
                    break;
            }
            var g = source.SampleGround(post.Stand);
            return !(g.HasWater && g.WaterSurface > g.Height);
        }

        /// <summary>One turtle folk at its post (counted with the settled ones), homed by its igloo's door.</summary>
        bool SpawnTurtle(in TerrainSource source, SpawnRow row, Post post, float4 door, ref Unity.Mathematics.Random r)
        {
            if (row.Species == null || settledCount >= MaxSettled)
                return false;
            var index = SpeciesIndex(row.Species);
            if (index < 0)
                return false;
            var worker = post.Task != CreatureTask.None;
            var p = post.Stand + r.NextFloat2Direction() * (worker ? 0.2f : 0.5f);
            var y = source.SampleHeight(p);
            var leash = worker ? 7f : math.max(2f, row.Range * 0.5f);
            var timer = r.NextFloat(40f, 120f);
            var yaw = math.atan2(post.Face.x - p.x, post.Face.y - p.y);
            Enqueue(index, new float3(p.x, y, p.y), SpawnTier.Settled, e =>
            {
                var em = simWorld.EntityManager;
                var state = em.GetComponentData<CreatureState>(e);
                state.Home = post.Stand;
                state.Leash = leash;
                state.OwnDoor = door;
                state.Job = (byte)post.Job;
                if (worker)
                {
                    state.Task = post.Task;
                    state.WorkSpot = post.Stand;
                    state.WorkFace = post.Face;
                    state.Mode = CreatureMode.Work;
                    state.Timer = timer;
                }
                em.SetComponentData(e, state);
            }, yaw);
            return true;
        }
    }
}
