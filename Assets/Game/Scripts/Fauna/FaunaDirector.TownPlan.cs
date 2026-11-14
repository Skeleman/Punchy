using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    // A-110 row 2: builds each town's plan (TownPlan) for Whereabouts from the baked records alone: its households,
    // the settlement's streets and plots, the anchors, and the landmark's resident rows. No random draw and nothing
    // read from the simulation, so every machine (a joined client too) builds the same plan. The ground grid is the
    // slow part (a few thousand terrain samples), so it is filled a few hundred samples a go.
    public partial class FaunaDirector
    {
        readonly List<Post> planPosts = new List<Post>();
        readonly List<(int a, int b)> planSegments = new List<(int, int)>();
        readonly List<(int node, float reach)> planRounds = new List<(int, float)>();
        readonly List<int> planJobs = new List<int>();
        readonly List<int> planOrder = new List<int>();
        TownPlan planPending;
        int planCursor;

        /// <summary>The ground grid's cell (m), and how far past the landmark's radius it reaches.</summary>
        const float PlanCell = 4f, PlanMargin = 20f;

        /// <summary>
        /// Starts a landmark's plan, if it is a town of houses with resident rows: the plan with its houses (from the
        /// middle out) and an empty ground grid. Null for anything else.
        /// </summary>
        TownPlan StartTownPlan(in TerrainSource source, int i)
        {
            ref var set = ref source.Landmarks.Value;
            ref var inst = ref set.Instances[i];
            var type = inst.Type < landmarks.Length ? landmarks[inst.Type] : null;
            if (type == null || type.Residents == null || type.Residents.Count == 0 ||
                !Households.ByLandmark.TryGetValue(i, out var houses) || houses.Count == 0)
                return null;
            // (Igloos, domes and the other home kinds are a later card: their folk aren't resident rows.)
            var kind = Households.All[houses[0]].Kind;
            if (kind != "house" && kind != "hut")
                return null;
            var radius = set.Types[inst.Type].Radius * inst.Scale;
            var plan = new TownPlan { Landmark = i, Name = type.name, Centre = inst.Position, Radius = radius };
            var centre = inst.Position;
            planOrder.Clear();
            planOrder.AddRange(houses);
            // (From the middle out; houses the same distance out keep their record order.)
            planOrder.Sort((a, b) =>
            {
                var da = math.distancesq(Households.All[a].Door.xz, centre);
                var db = math.distancesq(Households.All[b].Door.xz, centre);
                return da != db ? da.CompareTo(db) : a.CompareTo(b);
            });
            foreach (var h in planOrder)
            {
                if (Households.All[h].Members == null || Households.All[h].Members.Length == 0)
                    continue;
                plan.LocalOf[Households.All[h]] = plan.Houses.Count;
                plan.Houses.Add(h);
                plan.HouseResidents.Add(new List<int>());
            }
            if (plan.Houses.Count == 0)
                return null;
            var reach = radius + PlanMargin;
            plan.GridCell = PlanCell;
            plan.GridSize = (int)math.ceil(2f * reach / PlanCell) + 2;
            plan.GridMin = centre - reach;
            plan.Grid = new float[plan.GridSize * plan.GridSize];
            return plan;
        }

        /// <summary>Fills the next samples of a plan's ground grid. True once it is full.</summary>
        static bool FillTownGrid(in TerrainSource source, TownPlan plan, ref int cursor, int budget)
        {
            var n = plan.GridSize * plan.GridSize;
            for (; cursor < n && budget > 0; cursor++, budget--)
                plan.Grid[cursor] = FarGround(source, plan.GridMin + new float2(cursor % plan.GridSize, cursor / plan.GridSize) * plan.GridCell);
            return cursor >= n;
        }

        /// <summary>A landmark's whole plan in one go, not published (for checks that every build comes out the same).</summary>
        public TownPlan BuildTownPlanNow(in TerrainSource source, int landmark)
        {
            EnsureHouses(source);
            if (!Households.Ready || !source.Landmarks.IsCreated || landmark < 0 || landmark >= source.Landmarks.Value.Instances.Length)
                return null;
            var plan = StartTownPlan(source, landmark);
            if (plan == null)
                return null;
            var cursor = 0;
            FillTownGrid(source, plan, ref cursor, int.MaxValue);
            return FinishTownPlan(source, plan) ? plan : null;
        }

        static int PlanNode(in TerrainSource source, TownPlan plan, float2 at)
        {
            // (Street ends that meet are one node.)
            for (int n = 0; n < plan.Nodes.Count; n++)
                if (math.distancesq(plan.Nodes[n].xz, at) < 1.4f * 1.4f)
                    return n;
            plan.Nodes.Add(new float3(at.x, FarGround(source, at), at.y));
            plan.Links.Add(new List<(int, float)>());
            return plan.Nodes.Count - 1;
        }

        static void PlanLink(TownPlan plan, int a, int b)
        {
            if (a == b)
                return;
            foreach (var (to, _) in plan.Links[a])
                if (to == b)
                    return;
            var length = math.distance(plan.Nodes[a].xz, plan.Nodes[b].xz);
            plan.Links[a].Add((b, length));
            plan.Links[b].Add((a, length));
        }

        /// <summary>Adds a named place: its ground, the slope there (none where it is steep or broken), and the street
        /// node its ways start from (the nearest to nodeBy).</summary>
        static int PlanPlace(in TerrainSource source, TownPlan plan, PlaceKind kind, float2 at, float2 face, float spread, bool indoors,
                             int house, float2 nodeBy)
        {
            var height = FarGround(source, at);
            var slope = float2.zero;
            if (spread > 0f || kind == PlaceKind.Post || kind == PlaceKind.Gate)
            {
                slope = new float2(FarGround(source, at + new float2(1.5f, 0f)) - height, FarGround(source, at + new float2(0f, 1.5f)) - height) / 1.5f;
                if (math.cmax(math.abs(slope)) > 0.6f)
                    slope = float2.zero;
            }
            plan.Places.Add(new TownPlace
            {
                Kind = kind,
                At = at,
                Face = face,
                Slope = slope,
                Height = height,
                Spread = spread,
                Indoors = indoors,
                Node = plan.NodeNear(nodeBy, 30f),
                House = house
            });
            return plan.Places.Count - 1;
        }

        static bool PlanPlaceNear(TownPlan plan, float2 at, float within)
        {
            foreach (var p in plan.Places)
                if (p.Kind != PlaceKind.Home && p.Kind != PlaceKind.Step && math.distancesq(p.At, at) < within * within)
                    return true;
            return false;
        }

        /// <summary>
        /// The rest of a plan once its grid is full: the street graph, the named places, every household member, the
        /// jobs (handed to heads from the middle of town out, then to partners) and the watch. False if the town has
        /// no housed folk among its resident rows.
        /// </summary>
        bool FinishTownPlan(in TerrainSource source, TownPlan plan)
        {
            ref var set = ref source.Landmarks.Value;
            ref var inst = ref set.Instances[plan.Landmark];
            var type = landmarks[inst.Type];
            var centre = plan.Centre;
            var radius = plan.Radius;

            // Who lives here: the first housed grown species and the first housed child species of the resident rows.
            int grownSpecies = -1, childSpecies = -1;
            foreach (var r in type.Residents)
            {
                var index = SpeciesIndex(r.Species);
                if (index < 0 || !HousedFolk(index, out var child))
                    continue;
                if (child && childSpecies < 0)
                    childSpecies = index;
                if (!child && grownSpecies < 0)
                    grownSpecies = index;
            }
            if (grownSpecies < 0)
                return false;
            if (childSpecies < 0)
                childSpecies = grownSpecies;

            // The street graph: the settlement's own streets and the road stretches it owns (not the tunnels).
            var settlement = -1;
            if (source.Settlements.IsCreated)
            {
                ref var towns = ref source.Settlements.Value;
                for (int s = 0; s < towns.Settlements.Length; s++)
                    if (towns.Settlements[s].Landmark == plan.Landmark)
                        settlement = s;
            }
            planSegments.Clear();
            planRounds.Clear();
            if (settlement >= 0)
            {
                ref var towns = ref source.Settlements.Value;
                ref var data = ref towns.Settlements[settlement];
                for (int j = data.FirstStreet; j < data.FirstStreet + data.StreetCount; j++)
                {
                    ref var street = ref towns.Streets[j];
                    if (street.Road == SettlementSampling.TunnelRoad)
                        continue;
                    if (math.distancesq(street.A.xz, street.B.xz) < 0.01f)
                        planRounds.Add((PlanNode(source, plan, street.A.xz), street.Width * 0.5f));
                    else
                    {
                        var a = PlanNode(source, plan, street.A.xz);
                        var b = PlanNode(source, plan, street.B.xz);
                        PlanLink(plan, a, b);
                        planSegments.Add((a, b));
                    }
                }
                // (An open space joins every street end on its rim; a street end on another street's run joins it.)
                foreach (var (node, reach) in planRounds)
                    for (int n = 0; n < plan.Nodes.Count; n++)
                        if (math.distance(plan.Nodes[n].xz, plan.Nodes[node].xz) < reach + 2f)
                            PlanLink(plan, node, n);
                for (int n = 0; n < plan.Nodes.Count; n++)
                    foreach (var (a, b) in planSegments)
                    {
                        if (n == a || n == b)
                            continue;
                        var pa = plan.Nodes[a].xz;
                        var ab = plan.Nodes[b].xz - pa;
                        var t = math.dot(plan.Nodes[n].xz - pa, ab) / math.max(math.lengthsq(ab), 1e-4f);
                        if (t > 0.05f && t < 0.95f && math.distance(plan.Nodes[n].xz, pa + ab * t) < 1.4f)
                        {
                            PlanLink(plan, n, a);
                            PlanLink(plan, n, b);
                        }
                    }
            }

            // Every house's door and step, and its members.
            int grown = 0, workers = 0;
            for (int local = 0; local < plan.Houses.Count; local++)
            {
                var h = plan.Houses[local];
                var house = Households.All[h];
                var door = house.Door.xz;
                var outward = new float2(math.sin(house.Door.w), math.cos(house.Door.w));
                var home = PlanPlace(source, plan, PlaceKind.Home, door, door + outward, 0f, true, h, door + outward * 6f);
                var step = PlanPlace(source, plan, PlaceKind.Step, door + outward * 2f, door + outward * 8f, 1.2f, false, h, door + outward * 6f);
                plan.Steps.Add(step);
                for (int m = 0; m < house.Members.Length; m++)
                {
                    var member = house.Members[m];
                    var child = member.Role == Kin.Child;
                    var r = new Resident
                    {
                        Index = plan.Residents.Count,
                        House = h,
                        Local = local,
                        Member = m,
                        Role = member.Role,
                        Child = child,
                        Seed = member.Individual,
                        Scale = child ? 0.62f : 1f,
                        Speed = child ? 1.6f : member.Role == Kin.Elder ? 1f : 1.3f,
                        Home = home,
                        Step = step,
                        Job = -1
                    };
                    FarColours(species[child ? childSpecies : grownSpecies], r.Seed, out r.Body, out r.Limbs);
                    plan.Residents.Add(r);
                    plan.HouseResidents[local].Add(r.Index);
                    if (!child)
                        grown++;
                    if (!child && member.Role != Kin.Elder)
                        workers++;
                }
            }

            // Jobs, in the order they are handed out: the resident rows' workers, the village posts, the shops and
            // halls, then a few gardens.
            planJobs.Clear();
            foreach (var r in type.Residents)
            {
                var index = SpeciesIndex(r.Species);
                if (index < 0 || r.Task == CreatureTask.None || !HousedFolk(index, out var child) || child)
                    continue;
                var at = LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(r.HomeRing, r.HomeAngle), radius);
                var face = LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(r.FaceRing, r.FaceAngle), radius);
                if ((r.Task == CreatureTask.Hammer || r.Task == CreatureTask.Chop) && source.Anchors.IsCreated &&
                    WorkAnchorNear(ref source.Anchors.Value, new float3(face.x, source.SampleHeight(face), face.y), 12f, out var stand, out var facing))
                {
                    at = stand;
                    face = stand + facing;
                }
                var count = math.max(0, r.Count.x);
                var place = PlanPlace(source, plan, PlaceKind.Post, at, face, count > 1 ? 1f : 0f, false, -1, at);
                for (int k = 0; k < count; k++)
                    planJobs.Add(place);
            }
            var ground = source.SampleGround(centre).Biomes.Dominant();
            var workSet = ground >= 0 && ground < biomes.Length ? FolkWorkSets.For(biomes[ground]) : null;
            VillagePosts(source, centre, radius, workers, workSet, planPosts);
            plan.WorkSet = workSet != null ? workSet.name : null;
            plan.WorkSetPosts = 0;
            if (workSet != null)
                foreach (var post in planPosts)
                    foreach (var entry in workSet.Jobs)
                        if (entry.Job == post.Job)
                            plan.WorkSetPosts++;
            foreach (var post in planPosts)
            {
                // (The guard keeps the gate: the watch's place by night.)
                var patrol = post.Task == CreatureTask.Patrol;
                var place = PlanPlace(source, plan, patrol ? PlaceKind.Gate : PlaceKind.Post, post.Stand,
                                      patrol ? post.Stand + (post.Stand - centre) : post.Face, 0f, false, -1, post.Stand);
                if (patrol && plan.Gate < 0)
                    plan.Gate = place;
                planJobs.Add(place);
            }
            var firstGarden = -1;
            if (settlement >= 0)
            {
                ref var towns = ref source.Settlements.Value;
                ref var data = ref towns.Settlements[settlement];
                for (int j = data.FirstPlot; j < data.FirstPlot + data.PlotCount; j++)
                {
                    ref var plot = ref towns.Plots[j];
                    var front = plot.Centre + plot.Facing * (plot.Half.y + 0.5f);
                    switch (plot.Role)
                    {
                        case PlotRole.Seat:
                        case PlotRole.Shrine:
                        case PlotRole.Inn:
                        case PlotRole.Smithy:
                        case PlotRole.Store:
                        case PlotRole.Workshop:
                        {
                            var place = PlanPlace(source, plan, PlaceKind.Shop, front, front + plot.Facing, 0f, true, -1, front + plot.Facing * 4f);
                            plan.Errands.Add(place);
                            planJobs.Add(place);
                            break;
                        }
                        case PlotRole.Market:
                        case PlotRole.Stable:
                        {
                            var place = PlanPlace(source, plan, PlaceKind.Shop, front + plot.Facing, plot.Centre, 1.5f, false, -1, front + plot.Facing * 4f);
                            plan.Errands.Add(place);
                            planJobs.Add(place);
                            break;
                        }
                        case PlotRole.Well:
                            plan.Wells.Add(PlanPlace(source, plan, PlaceKind.Well, plot.Centre, plot.Centre, 2.4f, false, -1, plot.Centre));
                            break;
                    }
                }
                for (int j = data.FirstPlot; j < data.FirstPlot + data.PlotCount; j++)
                {
                    ref var plot = ref towns.Plots[j];
                    if (plot.Role != PlotRole.Landscaping)
                        continue;
                    var place = PlanPlace(source, plan, PlaceKind.Garden, plot.Centre, plot.Centre + plot.Facing, math.cmin(plot.Half) * 0.6f, false, -1, plot.Centre);
                    plan.Errands.Add(place);
                    if (firstGarden < 0)
                        firstGarden = place;
                    if (place - firstGarden < 6)
                        planJobs.Add(place);
                }
                if (math.lengthsq(data.Water.xz) > 0.01f && !PlanPlaceNear(plan, data.Water.xz, 3f))
                    plan.Wells.Add(PlanPlace(source, plan, PlaceKind.Well, data.Water.xz, data.Water.xz, 2.4f, false, -1, data.Water.xz));
                // The square, and the other open spaces as squares (the big ones) and greens.
                if (data.Square > 0f)
                {
                    var place = PlanPlace(source, plan, PlaceKind.Square, data.Centre, data.Centre, data.Square * 0.4f, false, -1, data.Centre);
                    plan.Social.Add(place);
                    plan.Play.Add(place);
                }
                foreach (var (node, reach) in planRounds)
                {
                    var at = plan.Nodes[node].xz;
                    if (reach < 3f || PlanPlaceNear(plan, at, 3f))
                        continue;
                    var place = PlanPlace(source, plan, reach >= 6f ? PlaceKind.Square : PlaceKind.Green, at, at, reach * 0.7f, false, -1, at);
                    plan.Social.Add(place);
                    plan.Play.Add(place);
                }
            }
            if (plan.Play.Count == 0)
            {
                // (A camp with no laid-out square: the middle of it.)
                var place = PlanPlace(source, plan, PlaceKind.Square, centre, centre, radius * 0.25f, false, -1, centre);
                plan.Social.Add(place);
                plan.Play.Add(place);
            }

            // Benches, greens and the gate, from the anchors.
            int benches = 0, greens = 0;
            if (source.Anchors.IsCreated)
            {
                ref var anchors = ref source.Anchors.Value;
                var near = new NativeList<int>(32, Allocator.Temp);
                var at3 = new float3(centre.x, inst.BaseHeight, centre.y);
                for (int pass = 0; pass < 2; pass++)
                {
                    near.Clear();
                    Anchors.Near(ref anchors, at3, radius * 1.1f, pass == 0 ? AnchorKind.Sit : AnchorKind.Bench, ref near);
                    foreach (var a in near)
                    {
                        var anchor = anchors.Anchors[a];
                        if (benches >= 12 || PlanPlaceNear(plan, anchor.Position.xz, 1.5f))
                            continue;
                        benches++;
                        plan.Social.Add(PlanPlace(source, plan, PlaceKind.Bench, anchor.Position.xz, anchor.Position.xz + anchor.Facing, 0.6f, false, -1,
                                                  anchor.Position.xz));
                    }
                }
                near.Clear();
                Anchors.Near(ref anchors, at3, radius * 1.1f, AnchorKind.Green, ref near);
                foreach (var a in near)
                {
                    var anchor = anchors.Anchors[a];
                    if (greens >= 6 || PlanPlaceNear(plan, anchor.Position.xz, 3f))
                        continue;
                    greens++;
                    var place = PlanPlace(source, plan, PlaceKind.Green, anchor.Position.xz, anchor.Position.xz,
                                          math.clamp(anchor.Size * 0.7f, 1f, 8f), false, -1, anchor.Position.xz);
                    plan.Social.Add(place);
                    plan.Play.Add(place);
                }
                if (plan.Gate < 0)
                {
                    near.Clear();
                    Anchors.Near(ref anchors, at3, radius * 1.2f, AnchorKind.Gate, ref near);
                    var best = -1;
                    var bestD = float.MaxValue;
                    foreach (var a in near)
                    {
                        var d = math.distancesq(anchors.Anchors[a].Position.xz, centre);
                        if (d < bestD)
                        {
                            bestD = d;
                            best = a;
                        }
                    }
                    if (best >= 0)
                    {
                        var at = anchors.Anchors[best].Position.xz;
                        plan.Gate = PlanPlace(source, plan, PlaceKind.Gate, at, at + (at - centre), 0f, false, -1, at);
                    }
                }
                near.Dispose();
            }
            if (plan.Gate < 0 && settlement >= 0)
            {
                // (No gate built: where the main road leaves.)
                var road = source.Settlements.Value.Settlements[settlement].Road;
                if (math.lengthsq(road) > 0.01f)
                {
                    var at = centre + math.normalize(road) * radius * 0.8f;
                    plan.Gate = PlanPlace(source, plan, PlaceKind.Gate, at, at + road, 0f, false, -1, at);
                }
            }

            // The jobs to the heads, from the middle of town out, then to the partners.
            var job = 0;
            for (int pass = 0; pass < 2 && job < planJobs.Count; pass++)
                for (int local = 0; local < plan.Houses.Count && job < planJobs.Count; local++)
                    foreach (var index in plan.HouseResidents[local])
                        if (plan.Residents[index].Role == (pass == 0 ? Kin.Head : Kin.Partner) && job < planJobs.Count)
                        {
                            plan.Residents[index].Job = planJobs[job++];
                            break;
                        }

            // The watch: the heads of the six houses with the shortest walk to the gate, where there are folk enough to spare them.
            if (plan.Gate >= 0 && workers >= 8)
            {
                // (Nearest by the walk, not as the crow flies: a house just over the wall can be a long way round.)
                planOrder.Clear();
                for (int local = 0; local < plan.Houses.Count; local++)
                    planOrder.Add(local);
                planOrder.Sort((a, b) =>
                {
                    var da = plan.Walk(plan.Steps[a], plan.Gate);
                    var db = plan.Walk(plan.Steps[b], plan.Gate);
                    return da != db ? da.CompareTo(db) : a.CompareTo(b);
                });
                foreach (var local in planOrder)
                {
                    if (plan.Watch.Count >= 6)
                        break;
                    foreach (var index in plan.HouseResidents[local])
                        if (plan.Residents[index].Role == Kin.Head)
                        {
                            plan.Watch.Add(index);
                            break;
                        }
                }
            }
            plan.Made = $"{plan.Houses.Count} houses, {plan.Residents.Count} members ({grown} grown, {workers} of working age), " +
                        $"{plan.Nodes.Count} street nodes, {plan.Places.Count} places ({job} of {planJobs.Count} jobs taken, {plan.Wells.Count} wells, " +
                        $"{plan.Social.Count} to meet at, {plan.Errands.Count} errands, {benches} benches), gate {(plan.Gate >= 0 ? "yes" : "no")}, " +
                        $"watch {plan.Watch.Count}, settlement {(settlement >= 0 ? "yes" : "no")}, " +
                        $"ground {(ground >= 0 && ground < biomes.Length && biomes[ground] != null ? biomes[ground].name : "none")}, " +
                        $"work set {(workSet != null ? workSet.name : "none")} of {FolkWorkSets.All.Count}, {planPosts.Count} posts ({plan.WorkSetPosts} the set's)";
            return true;
        }
    }
}
