using System.Runtime.InteropServices;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Fauna
{
    /// <summary>
    /// Poses every creature (Burst, in parallel) into one list of segments and draws them all with a single
    /// instanced draw of a unit capsule: the shader stretches, tapers and places each instance. Every creature also
    /// has a few spare capsules after its body for fear marks: short lines flashing out above the head while it
    /// flees (collapsed to nothing the rest of the time). Web spinners have their web's threads after that, drawn
    /// while they sit in it: everything about the web follows from where the spider is and which way it faces, so
    /// replicas draw the same web with nothing extra sent. Roosts near the camera show a colony of sleeping bats hanging
    /// from the ceiling: no creatures, just a few capsules each (placed from the roost's index, so every machine draws
    /// the same colony), thinner at night when most are out.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CreatureRenderSystem : SystemBase
    {
        /// <summary>"-needcues" (a test flag): every creature not fleeing shows one of the four need cues, picked by its
        /// Individual, all the time, whatever its needs, so a shot can show them side by side.</summary>
        static readonly bool ForceNeedCues = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-needcues") >= 0;

        EntityQuery query;
        GraphicsBuffer buffer;
        MaterialPropertyBlock flatProps;
        static readonly int SegmentsId = Shader.PropertyToID("_Segments");
        static readonly int SegmentOffsetId = Shader.PropertyToID("_SegmentOffset");
        const int MarkCount = 3;
        /// <summary>How much bigger a need cue is drawn than a fear mark (#64): small enough to stay a detail, big enough to read at 10 m.</summary>
        const float CueScale = 3f;
        const int Spokes = 12, Rings = 6, Anchors = 4;
        const int WebCount = Spokes + Spokes * Rings + Anchors;
        const int MoundCount = 7;
        /// <summary>Capsules per nest: its floor, a ring of twigs (or grass), and the darkening under it.</summary>
        const int NestCount = 20;
        const int NestRim = 14;
        /// <summary>Room for the animal's lair: a burrow's mound or a nest.</summary>
        static int LairCount(ref CreatureBlob b) => b.Burrows ? MoundCount : b.Nests != NestSite.None ? NestCount : 0;
        /// <summary>Capsules in a lantern (bail, cap, glass, base), and room for whatever is held.</summary>
        const int LanternParts = 4;
        /// <summary>A lantern's light in the glow list: warm (linear rgb × intensity), w its reach (m); and how near the
        /// camera a lantern has to be to light anything.</summary>
        static readonly UnityEngine.Vector4 LanternLight = new UnityEngine.Vector4(1.6f, 0.9f, 0.4f, 9f);
        /// <summary>A torch's: redder, brighter and further.</summary>
        static readonly UnityEngine.Vector4 TorchLight = new UnityEngine.Vector4(2.4f, 1.0f, 0.3f, 11f);
        /// <summary>How far off a lantern lights the ground (m; the graphics settings' Detail scales these ranges).</summary>
        static float LanternLightRange => Punchy.Settings.GraphicsSettings.DetailRange(60f);
        /// <summary>How far off a torch's flame is lit (further than its light reaches: a torch is seen across a town).</summary>
        static float TorchFlameRange => Punchy.Settings.GraphicsSettings.DetailRange(150f);
        /// <summary>How near the camera feet are fitted to the ground (CreatureAnimator.Evaluate's detail).</summary>
        static float FootDetailRange => Punchy.Settings.GraphicsSettings.DetailRange(40f);
        /// <summary>A torch's flame: its height (m), and how far it streams back as the bearer moves (m at the tip per
        /// m/s, at most).</summary>
        const float TorchFlameSize = 0.35f, TorchFlameLean = 0.05f, TorchFlameMaxLean = 0.3f;
        /// <summary>
        /// Capsules in a guard's kit: what it holds (a weapon, or a torch), a shield or a bow on its back, its helmet,
        /// breastplate, sash and shoulder pads. The torch's handle is always the first (its flame is lit from it).
        /// </summary>
        const int GuardParts = 18;
        /// <summary>Creatures behind the camera nearer than this are still posed (their shadows can fall in view).</summary>
        const float BehindCullRange = 25f;
        /// <summary>Capsules in a miner's gear: its pick (the haft, the head's two points) and a sack of ore.</summary>
        const int MinerParts = 4;
        /// <summary>When in a throw (its ActionTime) the stone leaves the hand: TraceSystem throws it from there.</summary>
        public const float ThrowRelease = 0.3f;

        /// <summary>Capsules in a fisher's gear: the rod, the line, the float, a fish, and the basket (its staves, its
        /// rim, the dark inside and the base).</summary>
        const int BasketStaves = 8;
        const int RodParts = 4 + BasketStaves * 2 + 2;
        static int HeldCount(HeldItem held, CreatureTask task) => task == CreatureTask.Hunt && (held == HeldItem.Spear || held == HeldItem.SpearDrawn) ? 0 :
                                               held == HeldItem.Lantern ? LanternParts : held == HeldItem.Spear || held == HeldItem.SpearDrawn || held == HeldItem.Torch ? GuardParts :
                                               held == HeldItem.Rod ? RodParts : held == HeldItem.Stick || held == HeldItem.Stone ? 1 :
                                               held == HeldItem.Pickaxe || held == HeldItem.OreSack ? MinerParts : held == HeldItem.Staff || held == HeldItem.Letter ? 2 : held == HeldItem.Honey ? 1 : held == HeldItem.Goggles ? 3 : 0;
        /// <summary>
        /// Capsules for a worker's gear, from its job alone (already on every machine, nothing more sent): the tool in
        /// hand while at work (a smith's hammer, a woodcutter's axe, a water-carrier's bucket), and what it wears (an
        /// apron for smiths and stallholders, a straw hat for those working outdoors, a fisher's hat). Since A-33 the job
        /// (CreatureState.Job, else the one the task implies) picks the props too: a baker's dough, table and cap, a
        /// healer's bowl and satchel, a well keeper's rope and bucket, a gatekeeper's spear, a smith's tongs and hot iron,
        /// a woodcutter's chopping block, a farmer's hoe, a trader's wares in hand. Slots: 0-1 the tool, 2 the apron,
        /// 3-4 the hat, 5-7 extras.
        /// </summary>
        const int GearParts = 8;
        static bool HasGear(CreatureTask task) => task == CreatureTask.Hammer || task == CreatureTask.Chop || task == CreatureTask.Tend ||
                                                  task == CreatureTask.Gesture || task == CreatureTask.Fish || task == CreatureTask.Hermit ||
                                                  task == CreatureTask.Watch;
        static int GearCount(CreatureTask task) => HasGear(task) ? GearParts : 0;
        /// <summary>Capsules for what a player's body has on (P-23): a hat's brim and crown, a pack.</summary>
        const int WornParts = 3;
        static int WornCount(byte worn) => worn != 0 ? WornParts : 0;
        /// <summary>Where a held item's light comes from among its capsules (the lantern's glass, the torch's flame).</summary>
        static int LightPart(HeldItem held) => held == HeldItem.Lantern ? 2 : held == HeldItem.Torch ? 0 : -1;
        /// <summary>Capsules per hanging bat, the most per roost, and how near the camera colonies show.</summary>
        const int BatParts = 4, ColonyMax = 10;
        static float ColonyRange => Punchy.Settings.GraphicsSettings.DetailRange(45f);
        Unity.Collections.NativeList<int> roosts;
        /// <summary>
        /// Bees at the wild hives near the camera (Hive anchors): drawn only, like the colonies, never simulated or
        /// sent. By day SwarmRound circle the comb and SwarmForagers fly out to the ground near it and back; at night a
        /// few crawl on the comb. Someone coming close sets SwarmChasers on them for a few seconds.
        /// </summary>
        const int SwarmRound = 26, SwarmForagers = 6, SwarmNight = 8, SwarmChasers = 9;
        /// <summary>(How near the camera they're drawn scales with the graphics settings; how near someone sets them off
        /// and for how long doesn't.)</summary>
        static float SwarmRange => Punchy.Settings.GraphicsSettings.DetailRange(40f);
        const float SwarmAngerDistance = 5f, SwarmChaseTime = 6f, SwarmCalmTime = 20f;
        Unity.Collections.NativeList<int> hives;
        /// <summary>When each hive (by anchor index) last set its bees on someone: per machine, for looks only.</summary>
        readonly System.Collections.Generic.Dictionary<int, float> hiveAngry = new System.Collections.Generic.Dictionary<int, float>();
        /// <summary>The bear (entity index) a hive's bees are after, if it was a bear that set them off.</summary>
        readonly System.Collections.Generic.Dictionary<int, int> hiveRaider = new System.Collections.Generic.Dictionary<int, int>();
        /// <summary>#63: the ground height under each forager's flower (hive anchor index * 64 + bee), sampled once,
        /// not every frame; forgotten when the island changes.</summary>
        static readonly System.Collections.Generic.Dictionary<int, float> foragerGround = new System.Collections.Generic.Dictionary<int, float>();
        static TerrainSource foragerSource;
        /// <summary>How many forager spots were sampled since the start (A-65 check against per-frame sampling).</summary>
        public static int ForagerGroundSamples;

        protected override void OnCreate()
        {
            query = GetEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            RequireForUpdate<TerrainSource>();
            RequireForUpdate(query);
            prevIndex = new NativeParallelHashMap<Entity, int>(256, Allocator.Persistent);
            noSegments = new NativeArray<SegmentInstance>(0, Allocator.Persistent);
            noOffsets = new NativeArray<int>(0, Allocator.Persistent);
            noStates = new NativeArray<CreatureState>(0, Allocator.Persistent);
            noBodies = new NativeArray<CreatureBody>(0, Allocator.Persistent);
            noEntities = new NativeArray<Entity>(0, Allocator.Persistent);
            noDecisions = new NativeArray<byte>(0, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            DropPosed();
            prevIndex.Dispose();
            noSegments.Dispose();
            noOffsets.Dispose();
            noStates.Dispose();
            noBodies.Dispose();
            noEntities.Dispose();
            noDecisions.Dispose();
            if (roosts.IsCreated) roosts.Dispose();
            if (hives.IsCreated) hives.Dispose();
            buffer?.Release();
            buffer = null;
        }

        protected override void OnUpdate()
        {
            if (!SystemAPI.ManagedAPI.TryGetSingleton<FaunaRenderSettings>(out var settings) || settings.Material == null || settings.Capsule == null)
            {
                DropPosed();
                return;
            }
            var cam = Camera.main;
            if (cam == null)
            {
                DropPosed();
                return;
            }
            var windDirection = Shader.GetGlobalVector("_PunchyWindDirection");
            CreatureAnimator.Wind.Data = new float4(Shader.GetGlobalFloat("_PunchyWind"), windDirection.x, windDirection.y,
                                                    Shader.GetGlobalFloat("_PunchyClock"));

            // Last frame's posing, done on the workers since then: lit, uploaded and drawn now (FinishPosed).
            PassStart(0);
            FinishPosed(cam, settings);

            // The creatures, as CreatureSnapshotSystem's copy job makes them this frame (A-70 row 3): the cull below
            // waits on its handle on the workers, so the main thread doesn't stop for the brain here. Copied out here
            // instead when HeldPoseSystem has moved carried ones since that copy (or with no copy this frame).
            var heldMoved = HeldPoseSystem.Ran.Data != 0;
            HeldPoseSystem.Ran.Data = 0;
            var ownCopy = !CreatureSnapshotSystem.Current(World, out var allStates, out var allBodies, out var allEntities, out var copied) || heldMoved;
            if (ownCopy)
            {
                var copyStart = System.Diagnostics.Stopwatch.GetTimestamp();
                allBodies = query.ToComponentDataArray<CreatureBody>(Allocator.TempJob);
                allStates = query.ToComponentDataArray<CreatureState>(Allocator.TempJob);
                allEntities = query.ToEntityArray(Allocator.TempJob);
                CreatureSnapshotSystem.CopyTicks += System.Diagnostics.Stopwatch.GetTimestamp() - copyStart;
                CreatureSnapshotSystem.Copies += 3;
                copied = default;
            }
            // (The kept ones are packed into these: the counts below and the pose job both read them.)
            var bodies = new NativeArray<CreatureBody>(allBodies.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var states = new NativeArray<CreatureState>(allBodies.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var entities = new NativeArray<Entity>(allBodies.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            // Deep in a cave, out of sight of every mouth (CaveSystem.SurfaceHidden): the creatures up on the surface are
            // neither posed nor drawn. (Players' bodies always are.)
            // And anything well behind the camera (past BehindCullRange, so the shadows of those close behind still fall
            // in view) is neither posed nor drawn: about half of them at any moment, the biggest share of the pose cost.
            var hidden = Punchy.Terrain.CaveSystem.SurfaceHidden >= 0.99f;
            SystemAPI.TryGetSingleton<TerrainSource>(out var hiddenFrom);
            float3 camPos = cam.transform.position, camFwd = cam.transform.forward;
            // (Behind: more than about 100 degrees off the way the camera looks, well outside any view it has.)
            const float behindCos = -0.17f;
            PassStart(bodies.Length);
            Pass(0);
            // Which to keep (CullJob, in parallel: in a cave every creature's ground height is sampled, A-22, #23), then the
            // kept ones packed down and their parts counted (CompactJob), all in Burst on the workers (A-51, #62) while the
            // main thread gathers what doesn't need the creatures: the small life, the birds overhead, cave fish, traces.
            var keepFlags = new NativeArray<bool>(bodies.Length, Allocator.TempJob);
            var offsets = new NativeArray<int>(bodies.Length, Allocator.TempJob);
            var hanging = new NativeList<float3>(8, Allocator.TempJob);
            var emoting = new NativeList<float3>(2, Allocator.TempJob);
            var counted = new NativeReference<int2>(Allocator.TempJob);
            var culling = default(JobHandle);
            var tiers = !OldPoseRate;
            // Each kept creature's detail (refactor 3.7: capsule, pose rate and shadow, by how big it looks), and where
            // each draw group starts among the parts.
            Punchy.Settings.DetailPolicy.SetView(cam);
            var decisions = new NativeArray<byte>(bodies.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var groupStarts = new NativeArray<int>(GroupCount + 1, Allocator.TempJob);
            UnityEngine.GeometryUtility.CalculateFrustumPlanes(cam, frustumPlanes);
            static float4 PlaneOf(UnityEngine.Plane plane) => new float4(plane.normal, plane.distance);
            if (OldCull)
            {
                copied.Complete();
                for (int i = 0; i < bodies.Length; i++)
                {
                    var to = allStates[i].Position - camPos;
                    var far = math.lengthsq(to) > BehindCullRange * BehindCullRange;
                    var behind = far && math.dot(math.normalizesafe(to), camFwd) < behindCos &&
                                 math.length(to) > allBodies[i].Blob.Value.SeenFrom * 0.5f;
                    var underground = hidden && !(allStates[i].Position.y < hiddenFrom.SampleHeight(allStates[i].Position.xz) - 1f);
                    keepFlags[i] = allStates[i].Mode == CreatureMode.Puppet || !behind && !underground;
                }
            }
            else
                culling = new CullJob
                {
                    States = allStates,
                    Bodies = allBodies,
                    Terrain = hiddenFrom,
                    CameraPosition = camPos,
                    CameraForward = camFwd,
                    BehindCos = behindCos,
                    Hidden = hidden,
                    Frustum = tiers,
                    Left = PlaneOf(frustumPlanes[0]),
                    Right = PlaneOf(frustumPlanes[1]),
                    Bottom = PlaneOf(frustumPlanes[2]),
                    Top = PlaneOf(frustumPlanes[3]),
                    Keep = keepFlags
                }.Schedule(bodies.Length, 16, copied);
            // (Where each creature was in last frame's posing, by entity: for its last decision here, and for those
            // moved on from their last pose in PoseJob, A-70 row 4.)
            var usePrev = tiers && havePrev;
            var indexing = new IndexJob { Entities = usePrev ? prevPosed.Entities : noEntities, Index = prevIndex }.Schedule();
            culling = new CompactJob
            {
                AllBodies = allBodies,
                AllStates = allStates,
                AllEntities = allEntities,
                Bodies = bodies,
                States = states,
                Entities = entities,
                Keep = keepFlags,
                Offsets = offsets,
                Hanging = hanging,
                Emoting = emoting,
                Counted = counted,
                Tiers = tiers,
                View = Punchy.Settings.DetailPolicy.Current.Data,
                PrevIndex = prevIndex,
                PrevDecisions = usePrev ? prevPosed.Decisions : noDecisions,
                Decisions = decisions,
                GroupStarts = groupStarts
            }.Schedule(JobHandle.CombineDependencies(culling, indexing));

            var terrain = SystemAPI.GetSingleton<TerrainSource>();
            float3 camAt0 = cam.transform.position;
            // The small life round the listener (AmbientLife: songbirds, owls, crickets, frogs...), drawn only.
            var critters = AmbientLife.Near(World, camAt0);
            var extraCount = critters.Count * CritterParts;
            // Birds high overhead (geese and cranes in a V, soarers on a thermal), drawn only, from the shared clock.
            var skyClock = SkyClock(out var skyHour);
            extraCount += GatherSkyBirds(camAt0, skyClock, skyHour, terrain);
            // Fish in the caves' pools near the camera, drawn only.
            extraCount += GatherCaveFish(terrain, camAt0) * FishParts;
            // Footprints and feathers (TraceSystem).
            var traces = World.GetExistingSystemManaged<TraceSystem>();
            var traceCount = traces != null ? traces.Count : 0;
            extraCount += traceCount;
            Pass(3);

            var waitStart = System.Diagnostics.Stopwatch.GetTimestamp();
            culling.Complete();
            CreatureSnapshotSystem.WaitTicks += System.Diagnostics.Stopwatch.GetTimestamp() - waitStart;
            keepFlags.Dispose();
            if (ownCopy)
            {
                allBodies.Dispose();
                allStates.Dispose();
                allEntities.Dispose();
            }
            var keep = counted.Value.x;
            var total = counted.Value.y;
            counted.Dispose();
            if (keep < bodies.Length)
            {
                var b2 = new NativeArray<CreatureBody>(keep, Allocator.TempJob);
                var s2 = new NativeArray<CreatureState>(keep, Allocator.TempJob);
                var e2 = new NativeArray<Entity>(keep, Allocator.TempJob);
                var d2 = new NativeArray<byte>(keep, Allocator.TempJob);
                NativeArray<CreatureBody>.Copy(bodies, b2, keep);
                NativeArray<CreatureState>.Copy(states, s2, keep);
                NativeArray<Entity>.Copy(entities, e2, keep);
                NativeArray<byte>.Copy(decisions, d2, keep);
                bodies.Dispose();
                states.Dispose();
                entities.Dispose();
                decisions.Dispose();
                bodies = b2;
                states = s2;
                entities = e2;
                decisions = d2;
            }
            Pass(1);
            SwingDoors(states);

            Pass(2);
            // Colonies at the roosts near the camera.
            if (!roosts.IsCreated)
                roosts = new Unity.Collections.NativeList<int>(16, Allocator.Persistent);
            roosts.Clear();
            if (terrain.Anchors.IsCreated)
                Punchy.Terrain.Anchors.Near(ref terrain.Anchors.Value, cam.transform.position, ColonyRange, AnchorKind.Roost, ref roosts);
            var night = SystemAPI.TryGetSingleton<FaunaClock>(out var clock) ? clock.Night : 0f;
            // (Less any real bats hanging there now, so they don't double up with the drawn ones.)
            var colonyStart = total;
            var colonySizes = new NativeArray<int>(roosts.Length, Allocator.Temp);
            var colonyFades = new NativeArray<float>(roosts.Length, Allocator.Temp);
            var now = (float)SystemAPI.Time.ElapsedTime;
            for (int i = 0; i < roosts.Length; i++)
            {
                var roost = terrain.Anchors.Value.Anchors[roosts[i]];
                var real = 0;
                foreach (var p in hanging)
                    if (math.distance(p, roost.Position) < roost.Size + 0.6f)
                        real++;
                colonyFades[i] = ColonyFade(roosts[i], roost, emoting, now);
                colonySizes[i] = colonyFades[i] >= 1f ? 0 : math.max(ColonySize(roost, roosts[i], night) - real, 0);
                total += colonySizes[i] * BatParts;
            }
            hanging.Dispose();
            emoting.Dispose();

            // Bees at the hives near the camera, set on whoever comes too close by day.
            if (!hives.IsCreated)
                hives = new Unity.Collections.NativeList<int>(4, Allocator.Persistent);
            hives.Clear();
            if (terrain.Anchors.IsCreated)
                Punchy.Terrain.Anchors.Near(ref terrain.Anchors.Value, cam.transform.position, SwarmRange, AnchorKind.Hive, ref hives);
            var day = night < 0.5f;
            var swarmPlayer = new float3(float.MaxValue);
            foreach (var t in SystemAPI.Query<RefRO<Unity.Transforms.LocalTransform>>().WithAll<Punchy.Player.PlayerTag>())
                swarmPlayer = t.ValueRO.Position;
            var swarmTargets = new NativeArray<float3>(hives.Length, Allocator.Temp);
            for (int i = 0; i < hives.Length; i++)
            {
                var hive = terrain.Anchors.Value.Anchors[hives[i]];
                var last = hiveAngry.TryGetValue(hives[i], out var since) ? since : -1000f;
                swarmTargets[i] = swarmPlayer;
                // A bear at the comb: mobbed for as long as it's there (and a few seconds after, as it makes off).
                var raider = -1;
                for (int k = 0; k < states.Length && raider < 0; k++)
                    if (states[k].Mode == CreatureMode.Work && (states[k].Stage == CreatureAnimator.RaidAtComb || states[k].Stage == CreatureAnimator.RaidLick) &&
                        math.distance(states[k].Position.xz, hive.Position.xz) < hive.Size + 2f)
                        raider = k;
                if (raider >= 0)
                {
                    hiveAngry[hives[i]] = now - 1f;
                    hiveRaider[hives[i]] = entities[raider].Index;
                }
                else if (day && now - last > SwarmChaseTime + SwarmCalmTime && math.distance(swarmPlayer, hive.Position) < SwarmAngerDistance + hive.Size)
                {
                    hiveAngry[hives[i]] = now;
                    hiveRaider.Remove(hives[i]);
                }
                // (Still after the bear as it goes, while the chase lasts.)
                if (hiveRaider.TryGetValue(hives[i], out var bearIndex))
                    for (int k = 0; k < states.Length; k++)
                        if (entities[k].Index == bearIndex)
                            swarmTargets[i] = states[k].Position;
                total += day ? SwarmRound + SwarmForagers : SwarmNight;
            }
            // Then the small life, the birds, the fish and the traces gathered above, in that order after them.
            total += extraCount;
            Pass(4);

            var instances = new NativeArray<SegmentInstance>(total, Allocator.TempJob);
            var stats = new NativeArray<long>(Unity.Jobs.LowLevel.Unsafe.JobsUtility.ThreadIndexCount * StatStride, Allocator.TempJob);
            // Over the copies, a few creatures per batch, so every worker takes a share (per chunk, it was one or two).
            var posing = new PoseJob
            {
                States = states,
                Bodies = bodies,
                Entities = entities,
                Terrain = SystemAPI.GetSingleton<TerrainSource>(),
                Offsets = offsets,
                Output = instances,
                Time = (float)SystemAPI.Time.ElapsedTime,
                CameraRight = cam.transform.right,
                ForceCues = ForceNeedCues,
                CameraPosition = cam.transform.position,
                FootDetail = FootDetailRange,
                FadeInTime = 1.2f,
                FadeOutRange = 25f,
                // A little short of FaunaDirector's despawn distance (170 m), so it's gone before it's removed.
                FadeFar = 165f,
                Night = night,
                PrevOutput = usePrev ? prevPosed.Instances : noSegments,
                PrevOffsets = usePrev ? prevPosed.Offsets : noOffsets,
                PrevStates = usePrev ? prevPosed.States : noStates,
                PrevBodies = usePrev ? prevPosed.Bodies : noBodies,
                PrevIndex = prevIndex,
                Tiers = tiers,
                Decisions = decisions,
                Frame = poseFrame++,
                Stats = stats
            }.Schedule(bodies.Length, 8, indexing);
            Pass(5);
            // While the creatures are posed on the workers, the rest is drawn here into its own array (the colonies, the
            // bees, the small life, the birds overhead, the traces), copied in after them once they're done, rather than
            // the main thread waiting on the posing first.
            var extras = new NativeArray<SegmentInstance>(total - colonyStart, Allocator.TempJob);
            var at = 0;
            for (int i = 0; i < roosts.Length; i++)
                at = Colony(terrain.Anchors.Value.Anchors[roosts[i]], roosts[i], colonySizes[i], colonyFades[i], extras, at);
            colonySizes.Dispose();
            colonyFades.Dispose();
            if (!foragerSource.Equals(terrain))
            {
                foragerGround.Clear();
                foragerSource = terrain;
            }
            for (int i = 0; i < hives.Length; i++)
            {
                var hive = terrain.Anchors.Value.Anchors[hives[i]];
                var angry = hiveAngry.TryGetValue(hives[i], out var since) ? now - since : -1f;
                var faded = math.saturate((math.distance(camAt0, hive.Position) - SwarmRange * 0.75f) / (SwarmRange * 0.25f));
                at = Swarm(hive, hives[i], day, now, angry, swarmTargets[i], faded, terrain, extras, at);
            }
            swarmTargets.Dispose();
            foreach (var critter in critters)
                at = Critter(critter, (float)SystemAPI.Time.ElapsedTime, extras, at);
            at = WriteSkyBirds(skyClock, extras, at);
            at = WriteCaveFish(skyClock, extras, at);
            if (traceCount > 0)
                at = traces.Write(extras, at, (float)SystemAPI.Time.ElapsedTime);
            Pass(6);
            // Not waited for (A-51, #62): the posing runs on the workers until the start of next frame's update, which
            // lights, uploads and draws it then (a frame late, which can't be seen).
            posed = new Posed
            {
                Job = posing,
                Instances = instances,
                Extras = extras,
                Offsets = offsets,
                Bodies = bodies,
                States = states,
                Entities = entities,
                ColonyStart = colonyStart,
                Total = total,
                Stats = stats,
                Decisions = decisions,
                GroupStarts = groupStarts
            };
            havePosed = true;
            PassEnd(bodies.Length, colonyStart, total, hidden);
        }

        [BurstCompile]
        partial struct PoseJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<CreatureState> States;
            [ReadOnly] public NativeArray<CreatureBody> Bodies;
            [ReadOnly] public NativeArray<Entity> Entities;
            public TerrainSource Terrain;
            [ReadOnly] public NativeArray<int> Offsets;
            [NativeDisableParallelForRestriction] public NativeArray<SegmentInstance> Output;
            public float Time;
            public Unity.Mathematics.float3 CameraRight, CameraPosition;
            /// <summary>"-needcues": every creature shows a need cue all the time (for shots of #64).</summary>
            public bool ForceCues;
            /// <summary>How near the camera feet are fitted to the ground (FootDetailRange, at the graphics settings).</summary>
            public float FootDetail;
            /// <summary>Faded in over this long after it's made; out over this far before it's let go.</summary>
            public float FadeInTime, FadeOutRange, FadeFar;
            /// <summary>0 by day, 1 at night (ghosts are only seen at night).</summary>
            public float Night;
            /// <summary>A-70 row 4: last frame's posing (its instances, offsets and creatures, and where each entity was
            /// in it), which creatures not due a fresh pose this frame move on from (Tiers), and this frame's number.</summary>
            [ReadOnly] public NativeArray<SegmentInstance> PrevOutput;
            [ReadOnly] public NativeArray<int> PrevOffsets;
            [ReadOnly] public NativeArray<CreatureState> PrevStates;
            [ReadOnly] public NativeArray<CreatureBody> PrevBodies;
            [ReadOnly] public NativeParallelHashMap<Entity, int> PrevIndex;
            public bool Tiers;
            /// <summary>Each creature's detail decision (DetailPolicy.Decision, packed), from CompactJob.</summary>
            [ReadOnly] public NativeArray<byte> Decisions;
            public int Frame;
            /// <summary>Per worker, StatStride apart: ticks, posed afresh, moved on (for -perflog).</summary>
            [NativeDisableParallelForRestriction] public NativeArray<long> Stats;
            [Unity.Collections.LowLevel.Unsafe.NativeSetThreadIndex] int threadIndex;

            public void Execute(int index)
            {
                var start = Unity.Profiling.LowLevel.Unsafe.ProfilerUnsafeUtility.Timestamp;
                var state = States[index];
                var body = Bodies[index];
                var entity = Entities[index];
                var offset = Offsets[index];
                // (Feet fitted to the ground only near the camera: past 40 m nobody can tell, and it's most of the cost.)
                // As it looks smaller (Decisions, refactor 3.7) a body is posed afresh only every 2nd frame, then every 4th
                // (staggered by entity), and between those its last pose is carried along to where it is now and turned as
                // it turns (A-70 row 4).
                var reused = Tiers && Reuse(state, body, entity, offset, Decisions[index]);
                if (!reused)
                    CreatureAnimator.Evaluate(ref body.Blob.Value, state, body.Coat, Terrain, Output, offset,
                                              Unity.Mathematics.math.distancesq(state.Position, CameraPosition) < FootDetail * FootDetail);
                RideSwell(state, body, offset);
                Rest(index, state, body, entity, offset);
                var slot = threadIndex * StatStride;
                Stats[slot] += Unity.Profiling.LowLevel.Unsafe.ProfilerUnsafeUtility.Timestamp - start;
                Stats[slot + (reused ? 2 : 1)] += 1;
            }

            /// <summary>Last frame's pose of this body moved on to where it is now, when it isn't due a fresh one.</summary>
            bool Reuse(in CreatureState state, in CreatureBody body, Entity entity, int offset, byte decision)
            {
                var rate = new Punchy.Settings.DetailPolicy.Decision { Packed = decision }.Animation;
                // (Nor swimmers and floaters: their pose rides the swell afresh each frame, RideSwell.)
                if (rate == 0 || body.Blob.Value.Haunts || body.Blob.Value.LavaDweller ||
                    body.Blob.Value.Habitat == Habitat.Swims || body.Blob.Value.Habitat == Habitat.Floats)
                    return false;
                var period = rate == 1 ? 2 : 4;
                if (((Frame + entity.Index) & (period - 1)) == 0)
                    return false;
                if (!PrevIndex.TryGetValue(entity, out var was) || PrevBodies[was].SegmentCount != body.SegmentCount)
                    return false;
                var prev = PrevStates[was];
                var from = PrevOffsets[was];
                var turn = quaternion.RotateY(state.Yaw - prev.Yaw);
                for (int i = 0; i < body.SegmentCount; i++)
                {
                    var seg = PrevOutput[from + i];
                    seg.Position = state.Position + math.mul(turn, seg.Position - prev.Position);
                    seg.Rotation = math.mul(turn, seg.Rotation);
                    seg.Pad = 0f;
                    Output[offset + i] = seg;
                }
                return true;
            }

            /// <summary>
            /// Playtest 11-08 item 20: a swimmer or floater rides the sea's swell (WaterSwells.SwellAt, the drawn water's
            /// own numbers), fully at the surface and less the deeper it swims, gone 3 m down. Drawn only: its simulated
            /// position stays on the flat surface, so nothing networked changes.
            /// </summary>
            void RideSwell(in CreatureState state, in CreatureBody body, int offset)
            {
                if (body.Blob.Value.Habitat != Habitat.Swims && body.Blob.Value.Habitat != Habitat.Floats)
                    return;
                var g = Terrain.SampleGround(state.Position.xz);
                if (!g.HasWater)
                    return;
                var rise = WaterSwells.Height(state.Position.xz, g.WaterSurface, g.Height)
                           * Unity.Mathematics.math.saturate(1f - (g.WaterSurface - state.Position.y) / 3f);
                if (rise == 0f)
                    return;
                for (int i = 0; i < body.SegmentCount; i++)
                {
                    var seg = Output[offset + i];
                    seg.Position.y += rise;
                    Output[offset + i] = seg;
                }
            }

            /// <summary>Everything after the body's own segments: grounding, fades, marks, lairs and what it holds.</summary>
            void Rest(int index, in CreatureState state, in CreatureBody body, Entity entity, int offset)
            {
                // (The ground it stands on, for the shader's grounding: darker where it meets it.)
                for (int i = 0; i < body.SegmentCount; i++)
                {
                    var seg = Output[offset + i];
                    seg.Material.y = state.Position.y;
                    Output[offset + i] = seg;
                }
                // Appearing (its first moments: Clock counts from when it was made, here) or nearing the distance
                // where it's let go: dithered in and out by the shader, so nothing pops.
                var fadeIn = Unity.Mathematics.math.saturate(state.Clock / FadeInTime);
                // (Huge animals, seen across the land, from their own distance.)
                var fadeFar = Unity.Mathematics.math.max(FadeFar, body.Blob.Value.SeenFrom);
                var fadeOut = Unity.Mathematics.math.saturate((fadeFar - Unity.Mathematics.math.distance(state.Position, CameraPosition)) / FadeOutRange);
                var faded = 1f - fadeIn * fadeOut;
                // Stepping through a door: dithered out going in, gone indoors, dithered back in coming out.
                if (state.Mode == CreatureMode.Home && state.Stage >= 1)
                    faded = Unity.Mathematics.math.max(faded, state.Stage == 2 ? 1f : state.ActionTime);
                // A ghost is only half there (shimmering), and not there at all by day.
                if (body.Blob.Value.Haunts)
                {
                    var shimmer = 0.42f + 0.1f * Unity.Mathematics.math.sin(Time * 1.3f + (state.Individual % 13));
                    var day = 1f - Unity.Mathematics.math.smoothstep(0.2f, 0.6f, Night);
                    faded = Unity.Mathematics.math.max(faded, Unity.Mathematics.math.max(shimmer, day));
                }
                // A camera right up against a body (the player's own, squeezed in by a cave wall or ceiling): dithered
                // out from 0.6 m off its nearest part, gone by 0.35 m, so the view isn't the inside of a head (A-32).
                var nearest = float.MaxValue;
                for (int i = 0; i < body.SegmentCount; i++)
                    nearest = Unity.Mathematics.math.min(nearest, Unity.Mathematics.math.distance(Output[offset + i].Position, CameraPosition));
                faded = Unity.Mathematics.math.max(faded, Unity.Mathematics.math.saturate((0.6f - nearest) / 0.25f));
                var haunts = body.Blob.Value.Haunts;
                if (faded > 0f)
                    for (int i = 0; i < body.SegmentCount; i++)
                    {
                        var instance = Output[offset + i];
                        instance.Pad = faded;
                        // (A ghost glows faintly all over, so the whole of it shows in the dark.)
                        if (haunts)
                            instance.Color.w = Unity.Mathematics.math.max(instance.Color.w, 0.3f);
                        Output[offset + i] = instance;
                    }
                // A lava dweller's bones glow like embers, all over, pulsing slowly.
                if (body.Blob.Value.LavaDweller)
                    for (int i = 0; i < body.SegmentCount; i++)
                    {
                        var instance = Output[offset + i];
                        instance.Color.w = Unity.Mathematics.math.max(instance.Color.w, 0.28f + 0.08f * Unity.Mathematics.math.sin(Time * 1.7f + i * 0.6f));
                        Output[offset + i] = instance;
                    }
                FearMarks(state, offset, body.SegmentCount, entity.Index);
                if (InWeb(state))
                    Web(state, ref body.Blob.Value, offset + body.SegmentCount + MarkCount);
                var lair = offset + body.SegmentCount + MarkCount + (InWeb(state) ? WebCount : 0);
                if (body.Blob.Value.Burrows)
                    Mound(state, lair);
                else if (body.Blob.Value.Nests != NestSite.None)
                    Nest(state, body.Blob.Value.Nests, lair);
                var held = lair + LairCount(ref body.Blob.Value) + (state.Task == CreatureTask.Hunt ? 1 : 0);
                if (state.Task == CreatureTask.Hunt)
                    Spear(state, ref body.Blob.Value, offset, held - 1);
                if (state.Held == HeldItem.Lantern)
                    Lantern(state, ref body.Blob.Value, offset, held, faded);
                else if (state.Task == CreatureTask.Patrol && (state.Held == HeldItem.Spear || state.Held == HeldItem.SpearDrawn || state.Held == HeldItem.Torch))
                    Guard(state, ref body.Blob.Value, offset, held, faded, entity.Index);
                else if (state.Held == HeldItem.Torch)
                    Torch(state, ref body.Blob.Value, offset, held, faded);
                else if (state.Held == HeldItem.Rod)
                    Rod(state, ref body.Blob.Value, offset, held, faded, entity.Index);
                else if (state.Held == HeldItem.Stick || state.Held == HeldItem.Stone)
                    Plaything(state, ref body.Blob.Value, offset, held, faded);
                else if (state.Held == HeldItem.Pickaxe || state.Held == HeldItem.OreSack)
                    Miner(state, ref body.Blob.Value, offset, held, faded);
                else if (state.Held == HeldItem.Staff)
                    Traveller(state, ref body.Blob.Value, offset, held, faded);
                else if (state.Held == HeldItem.Letter)
                    Letter(state, ref body.Blob.Value, offset, held, faded);
                else if (state.Held == HeldItem.Honey)
                    Honey(state, ref body.Blob.Value, offset, held, faded);
                else if (state.Held == HeldItem.Goggles)
                    Goggles(state, ref body.Blob.Value, offset, held, faded);
                if (HasGear(state.Task))
                    Gear(state, ref body.Blob.Value, offset, held + HeldCount(state.Held, state.Task), faded);
                if (state.Worn != 0)
                    Worn(state, ref body.Blob.Value, offset, held + HeldCount(state.Held, state.Task) + GearCount(state.Task), faded);
            }
        }
    }
}
