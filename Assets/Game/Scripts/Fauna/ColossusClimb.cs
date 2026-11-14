using System.Collections.Generic;
using Punchy.Settings;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>A-107: one colossus column near a player, for movement: where it stands, how wide and tall, and its
    /// climbing grade (Smooth where no climb section marks it).</summary>
    public struct ColossusClimbColumn
    {
        public float3 Base;
        public float Radius, Height;
        public byte Grade;
        /// <summary>Row 2: which colossus it is on (its slot), to carry a climber with it.</summary>
        public byte Slot;
    }

    /// <summary>
    /// A-107 (#276): the colossi's columns near the players this frame, graded by their definitions' climb sections
    /// (ColossusCollision.Mark), gathered by ColossusClimbSystem round every player, the joined ones too. Movement climbs
    /// a marked column like a trunk (GrabTrunk takes it anywhere up its height) and stands on any column's top, so a
    /// climber goes up a section column by column, topping out on each and taking hold of the next taller one, to the
    /// back or the head. The grades answer the Climbability query (WorldClimbs) like any marked wall. A shared list, so
    /// Burst movement reads it; seed-derived like the colossi, so every machine has the same.
    /// </summary>
    public struct ColossusClimbs : IClimbSections
    {
        public static readonly SharedStatic<FixedList4096Bytes<ColossusClimbColumn>> List =
            SharedStatic<FixedList4096Bytes<ColossusClimbColumn>>.GetOrCreate<ColossusClimbs, ListKey>();
        class ListKey { }

        /// <summary>How far round each player the columns are gathered (m): a column's width past a climb's reach and a
        /// step off a top. // gameplay: what the body can touch, not what is drawn.</summary>
        public const float Gather = 8f;

        public bool GradeNear(float3 point, float radius, out byte grade) => At(point, radius, out grade);

        /// <summary>The grade of the column whose surface is nearest point (within reach of it, between its foot and its
        /// top), if a climb section marks it; false on a smooth column or none.</summary>
        public static bool At(float3 point, float reach, out byte grade)
        {
            grade = Climbing.None;
            if (!math.all(math.isfinite(point)))
                return false;
            ref var list = ref List.Data;
            var best = float.MaxValue;
            for (int i = 0; i < list.Length; i++)
            {
                var c = list[i];
                var off = math.abs(math.distance(point.xz, c.Base.xz) - c.Radius);
                if (off <= reach && off < best && point.y >= c.Base.y - reach && point.y <= c.Base.y + c.Height + reach)
                {
                    best = off;
                    grade = c.Grade;
                }
            }
            if (grade >= Climbing.Smooth)
                grade = Climbing.None;
            return grade != Climbing.None;
        }

        /// <summary>The highest column top under feet (its middle within a column) no higher than a step up from them:
        /// what a body stands on, on a colossus.</summary>
        public static bool FloorUnder(float3 feet, float radius, float stepUp, out float top)
        {
            top = float.MinValue;
            if (!math.all(math.isfinite(feet)))
                return false;
            ref var list = ref List.Data;
            for (int i = 0; i < list.Length; i++)
            {
                var c = list[i];
                var height = c.Base.y + c.Height;
                if (height <= feet.y + stepUp && height > top && math.distance(feet.xz, c.Base.xz) <= c.Radius + radius * 0.25f)
                    top = height;
            }
            return top > float.MinValue;
        }

        /// <summary>Whether a flora obstacle is a colossus column (they share the obstacle buffer with the trunks). Near
        /// enough rather than exact: a walking colossus's columns may have moved on since the buffer was filled.</summary>
        public static bool IsColumn(float3 position, float radius)
        {
            ref var list = ref List.Data;
            for (int i = 0; i < list.Length; i++)
                if (math.abs(list[i].Radius - radius) < 1e-3f && math.distancesq(list[i].Base, position) < 1f)
                    return true;
            return false;
        }
    }

    /// <summary>
    /// A-107: gathers the colossus columns round every player, the local one and the joined ones, into ColossusClimbs
    /// before the player moves. Only where the player moves (a client or host's client world): the list is shared by
    /// every world in the process. Managed: the columns live in ColossusCollision (main thread).
    /// Row 2: first it carries the local player on with the colossus they hold or stand on, by how far its body walked
    /// and turned since the columns were last gathered (ColossusCollision.Carry), so a climber rides a walker with no
    /// slide and no jitter. The walker's place comes from the shared clock and the player's from its own machine
    /// (client-authoritative movement), so riding sends nothing: the others see the rider where the rider's machine put
    /// them. A big lurch (the hold's speed jumping by more than LurchKick in a frame) shakes off a climber with no skill
    /// to spare for the grade they hold; one with a grade to spare holds on.
    /// </summary>
    // (Only in the worlds movement runs in: a server world filling the shared list from its own players, after the
    // client world, left movement an empty list on some frames.)
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(Punchy.Player.PlayerMovementSystem))]
    public partial class ColossusClimbSystem : SystemBase
    {
        EntityQuery players, joined, local, bodies;

        /// <summary>The jump in a hold's speed in one frame (m/s) that shakes off a climber at their limit: a rock golem's
        /// footfall, not a tortoise's. // gameplay: a grip limit, not a visual range.</summary>
        public const float LurchKick = 0.6f;
        /// <summary>"-colossusclimb" (DebugControls): log the ride once a second (place, speed, biggest kick, turn).</summary>
        public static bool LogRide;
        /// <summary>"-ccpeers" (DebugControls): log once a second how far each joined player is drawn off the nearest
        /// colossus column here (0: on its top or at its side), for the host and client check of a ride.</summary>
        public static bool LogPeers;
        float peersAt;
        static readonly int ClockId = UnityEngine.Shader.PropertyToID("_PunchyClock");
        /// <summary>The colossus the local player rides this frame (its slot), or -1; ColossusWalker keeps walking under them.</summary>
        public static int Riding = -1;
        /// <summary>The colossi any player is up on this frame, the joined ones too (a bit per slot), told on this machine
        /// from the players' places it already has: a walker with a rider does not stop to look at anyone (A-74), on any
        /// machine, so the rider stays on the body in every view. Nothing is sent.</summary>
        static ulong ridden;
        public static bool AnyRider(int slot) => slot == Riding || slot >= 0 && slot < 64 && (ridden >> slot & 1) != 0;
        /// <summary>Up on a body: this far over a column's foot (m), and within this of its side (m), wider once on (a
        /// joined rider is drawn a little behind, and the body may be a little off its place on their machine).
        /// // gameplay: who counts as a rider, not what is drawn.</summary>
        const float RiderLift = 2.5f, RiderNear = 1.5f, RiderKeep = 4f;
        /// <summary>Lurches that shook the local player off, and the biggest kick ridden through, since the start (probes).</summary>
        public static int ShakenOff;
        public static float BiggestKick;

        int rode = -1;
        float3 lastVelocity;
        float logAt, logKick, logTurn;

        protected override void OnCreate()
        {
            RequireForUpdate<Punchy.Player.PlayerInput>();
            players = GetEntityQuery(ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<Punchy.Player.PlayerTag>());
            joined = GetEntityQuery(ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<Punchy.Net.NetPlayerLook>());
            local = GetEntityQuery(ComponentType.ReadWrite<LocalTransform>(), ComponentType.ReadWrite<Punchy.Player.PlayerMotion>(),
                                   ComponentType.ReadOnly<Punchy.Player.PlayerTag>());
            bodies = GetEntityQuery(ComponentType.ReadOnly<Punchy.Player.PlayerBody>());
        }

        protected override void OnDestroy() => ColossusClimbs.List.Data.Clear();

        protected override void OnUpdate()
        {
            ref var list = ref ColossusClimbs.List.Data;
            Ride(ref list);
            list.Clear();
            var near = new FixedList128Bytes<float2>();
            Add(players, ref near);
            Add(joined, ref near);
            ColossusCollision.GatherClimb(ref list, near, ColossusClimbs.Gather);
            var now = 0ul;
            Riders(players, ref list, ref now);
            Riders(joined, ref list, ref now);
            ridden = now;
            if (LogPeers && UnityEngine.Time.time >= peersAt && !joined.IsEmpty)
            {
                peersAt = UnityEngine.Time.time + 1f;
                using var at = joined.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                for (int p = 0; p < at.Length; p++)
                {
                    var feet = at[p].Position;
                    var off = float.MaxValue;
                    var slot = -1;
                    for (int i = 0; i < list.Length; i++)
                    {
                        var c = list[i];
                        // (Off its side by more than a body's width, or over its top.)
                        var gap = math.length(new float2(math.max(math.distance(feet.xz, c.Base.xz) - c.Radius - 0.5f, 0f),
                                                         math.max(math.max(feet.y - (c.Base.y + c.Height), c.Base.y - feet.y), 0f)));
                        if (gap < off)
                        {
                            off = gap;
                            slot = c.Slot;
                        }
                    }
                    var on = float3.zero;
                    var body = slot >= 0 && ColossusCollision.OnBody(slot, feet, out on);
                    // (Too far off for any column: the nearest gathered body all the same, to tell how far.)
                    for (int other = 0; slot < 0 && other < 64; other++)
                        if (ColossusCollision.OnBody(other, feet, out var from) && (!body || math.lengthsq(from) < math.lengthsq(on)))
                        {
                            on = from;
                            body = true;
                        }
                    Debug.Log($"ColossusClimbSystem: carry: peer {p} at ({feet.x:F1}, {feet.y:F1}, {feet.z:F1}): " +
                              (slot < 0 ? "no column within reach" : $"off slot {slot} by {off:F2} m, rider there {AnyRider(slot)}") +
                              (body ? $", on its body at ({on.x:F2}, {on.y:F2}, {on.z:F2})" : "") +
                              $", clock {UnityEngine.Shader.GetGlobalFloat(ClockId):F1} s");
                }
            }
        }

        /// <summary>Marks the colossi these players are up on: beside or on a column, well over its foot.</summary>
        static void Riders(EntityQuery query, ref FixedList4096Bytes<ColossusClimbColumn> list, ref ulong now)
        {
            if (query.IsEmpty)
                return;
            using var at = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int p = 0; p < at.Length; p++)
            {
                var feet = at[p].Position;
                if (!math.all(math.isfinite(feet)))
                    continue;
                for (int i = 0; i < list.Length; i++)
                {
                    var c = list[i];
                    if (c.Slot >= 64)
                        continue;
                    var reach = (ridden >> c.Slot & 1) != 0 ? RiderKeep : RiderNear;
                    if (feet.y >= c.Base.y + RiderLift && feet.y <= c.Base.y + c.Height + reach &&
                        math.distance(feet.xz, c.Base.xz) <= c.Radius + reach)
                        now |= 1ul << c.Slot;
                }
            }
        }

        /// <summary>
        /// Row 2: carries the local player with the colossus column they hold (climbing it) or stand on (its top), read
        /// from last frame's list, the columns as they stood when the player last moved against them.
        /// </summary>
        void Ride(ref FixedList4096Bytes<ColossusClimbColumn> list)
        {
            Riding = -1;
            if (local.CalculateEntityCount() != 1)
            {
                rode = -1;
                return;
            }
            var self = local.GetSingletonEntity();
            var t = EntityManager.GetComponentData<LocalTransform>(self);
            var m = EntityManager.GetComponentData<Punchy.Player.PlayerMotion>(self);
            var on = -1;
            var grade = Climbing.None;
            for (int i = 0; i < list.Length && on < 0; i++)
            {
                var c = list[i];
                if (m.OnTrunk)
                {
                    if (math.abs(c.Radius - m.TrunkRadius) < 1e-3f && math.distancesq(c.Base, m.TrunkBase) < 1f)
                    {
                        on = i;
                        grade = c.Grade;
                    }
                }
                // (Standing on its top: feet on it, within its circle.)
                else if (m.Grounded && math.abs(t.Position.y - (c.Base.y + c.Height)) < 0.25f && math.distance(t.Position.xz, c.Base.xz) <= c.Radius + 0.3f)
                    on = i;
            }
            if (on < 0 || !math.all(math.isfinite(t.Position)))
            {
                rode = -1;
                return;
            }
            var slot = list[on].Slot;
            Riding = slot;
            var dt = SystemAPI.Time.DeltaTime;
            if (!ColossusCollision.Carry(slot, t.Position, out var moved, out var turn) || dt <= 0f)
            {
                // (Not moving: standing at rest, or not posed this frame. The next frame starts the kick afresh.)
                rode = -1;
                return;
            }
            // The kick: how far the hold's speed is off its speed over the last tenth of a second (smoothed, so the same
            // footfall kicks alike at any frame rate).
            var velocity = (moved - t.Position) / dt;
            var kick = rode == slot ? math.length(velocity - lastVelocity) : 0f;
            lastVelocity = rode == slot ? math.lerp(lastVelocity, velocity, math.saturate(dt / 0.1f)) : velocity;
            rode = slot;
            t.Position = moved;
            t.Rotation = math.mul(quaternion.RotateY(turn), t.Rotation);
            if (m.OnTrunk)
            {
                ColossusCollision.Carry(slot, m.TrunkBase, out m.TrunkBase, out _);
                // (Its place round the column turns with the body: TrunkAngle runs the other way to a yaw.)
                m.TrunkAngle -= turn;
                var skill = bodies.TryGetSingleton<Punchy.Player.PlayerBody>(out var body) ? body.ClimbSkill : Climbing.HumanoidSkill;
                if (kick > LurchKick && grade > Climbing.None && skill - grade < 1)
                {
                    // Shaken off: let go.
                    m.OnTrunk = false;
                    m.LatchCooldown = 1f;
                    ShakenOff++;
                    Debug.Log($"ColossusClimbSystem: carry: a lurch of {kick:F2} m/s shook the climber off a grade {grade} hold (skill {skill})");
                }
                else
                    BiggestKick = math.max(BiggestKick, kick);
            }
            if (m.Mantle > 0f)
            {
                ColossusCollision.Carry(slot, m.MantleFrom, out m.MantleFrom, out _);
                ColossusCollision.Carry(slot, m.MantleTo, out m.MantleTo, out _);
            }
            EntityManager.SetComponentData(self, t);
            EntityManager.SetComponentData(self, m);
            logKick = math.max(logKick, kick);
            logTurn += math.degrees(turn);
            if (LogRide && UnityEngine.Time.time >= logAt)
            {
                logAt = UnityEngine.Time.time + 1f;
                Debug.Log($"ColossusClimbSystem: carry: on slot {slot} {(m.OnTrunk ? "climbing" : "standing")}, at ({t.Position.x:F1}, {t.Position.y:F1}, {t.Position.z:F1}), " +
                          $"speed {math.length(velocity):F2} m/s, biggest kick {logKick:F2} m/s, turned {logTurn:F1} deg");
                logKick = logTurn = 0f;
            }
        }

        static void Add(EntityQuery query, ref FixedList128Bytes<float2> near)
        {
            if (query.IsEmpty)
                return;
            using var at = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < at.Length && near.Length < near.Capacity; i++)
                if (math.all(math.isfinite(at[i].Position)))
                    near.Add(at[i].Position.xz);
        }
    }

    /// <summary>
    /// A-107: what makes a colossus's climb sections read as climbable, built once on its meshes where the sections
    /// mark them (ColossusCollision.SectionAt): cracks with worn handholds and moss in them, vines hanging down a flank,
    /// worn ledges with a bird's nest on one, fur tufts, moss patches. Seed-derived (the colossus's salt), so every
    /// machine sees the same. One mesh per section and part, a child of the part, so the details follow everything that
    /// moves it; each is shown by its size on screen (ColossusClimbDetail).
    /// </summary>
    public static class ColossusClimbDetails
    {
        /// <summary>Details per square metre of a section, and the most per section.</summary>
        const float CrackDensity = 0.3f, VineDensity = 0.08f, LedgeDensity = 0.06f, FurDensity = 1.1f, MossDensity = 0.35f;
        const int MostPerSection = 160;

        struct Face
        {
            public MeshFilter Part;
            public Vector3 A, B, C, Normal;
            public Color Color;
            public float Area;
        }

        /// <summary>Builds slot's details on the colossus under root, right after it was marked (at the pose it was
        /// built in). Parts named in skip, and the moving part, get none.</summary>
        public static void Build(int slot, Transform root, uint seed, Transform moving, string[] skip)
        {
            var sections = ColossusCollision.SectionsOf(slot);
            if (root == null || sections.Length == 0)
                return;
            var faces = new List<Face>[sections.Length];
            var areas = new float[sections.Length];
            for (int s = 0; s < sections.Length; s++)
                faces[s] = new List<Face>();
            var verts = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable || moving != null && filter.transform.IsChildOf(moving) || System.Array.IndexOf(skip, filter.name) >= 0)
                    continue;
                var toWorld = filter.transform.localToWorldMatrix;
                mesh.GetVertices(verts);
                mesh.GetColors(colors);
                for (int i = 0; i < verts.Count; i++)
                    verts[i] = toWorld.MultiplyPoint3x4(verts[i]);
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles)
                        continue;
                    mesh.GetTriangles(tris, sub);
                    for (int t = 0; t + 2 < tris.Count; t += 3)
                    {
                        var a = verts[tris[t]];
                        var b = verts[tris[t + 1]];
                        var c = verts[tris[t + 2]];
                        var cross = Vector3.Cross(b - a, c - a);
                        var area = cross.magnitude * 0.5f;
                        if (area < 0.05f)
                            continue;
                        var normal = cross / (area * 2f);
                        // (Faces looking down are undersides: nothing grows or nests there.)
                        if (normal.y < -0.35f)
                            continue;
                        var section = ColossusCollision.SectionAt(slot, (a + b + c) / 3f);
                        if (section < 0)
                            continue;
                        var color = colors.Count > tris[t] ? colors[tris[t]] : new Color(0.5f, 0.5f, 0.5f, 1f);
                        color.a = 1f;   // (alpha below 1 glows on the colossus material)
                        faces[section].Add(new Face { Part = filter, A = a, B = b, C = c, Normal = normal, Color = color, Area = area });
                        areas[section] += area;
                    }
                }
            }
            var builders = new Dictionary<(MeshFilter, int), DunesColossus.MeshBuilder>();
            var placed = 0;
            for (int s = 0; s < sections.Length; s++)
            {
                if (faces[s].Count == 0)
                    continue;
                var rng = Unity.Mathematics.Random.CreateFromIndex(seed ^ (uint)(0x5EC7 + s * 7919));
                var look = sections[s].Look;
                var density = look switch
                {
                    ClimbLook.Cracks => CrackDensity,
                    ClimbLook.Vines => VineDensity,
                    ClimbLook.Ledges => LedgeDensity,
                    ClimbLook.Fur => FurDensity,
                    _ => MossDensity
                };
                var count = math.clamp((int)(areas[s] * density), 3, MostPerSection);
                var nested = false;
                for (int n = 0; n < count; n++)
                {
                    var f = Pick(faces[s], areas[s], ref rng);
                    // (Ledges want a steep face to stick out of; everything else takes any face looking out.)
                    if (look == ClimbLook.Ledges && math.abs(f.Normal.y) > 0.6f)
                        continue;
                    var r1 = rng.NextFloat();
                    var r2 = rng.NextFloat();
                    if (r1 + r2 > 1f)
                    {
                        r1 = 1f - r1;
                        r2 = 1f - r2;
                    }
                    var at = f.A + (f.B - f.A) * r1 + (f.C - f.A) * r2;
                    var key = (f.Part, s);
                    if (!builders.TryGetValue(key, out var mb))
                        builders[key] = mb = new DunesColossus.MeshBuilder();
                    var toLocal = f.Part.transform.worldToLocalMatrix;
                    var local = (Vector3)toLocal.MultiplyPoint3x4(at);
                    var normal = ((Vector3)toLocal.MultiplyVector(f.Normal)).normalized;
                    var scale = f.Part.transform.lossyScale.x;
                    var size = 1f / math.max(scale, 0.01f);
                    switch (look)
                    {
                        case ClimbLook.Cracks: Crack(mb, local, normal, f.Color, size, ref rng); break;
                        case ClimbLook.Vines: Vine(mb, local, normal, (Vector3)toLocal.MultiplyVector(Vector3.down), f.Color, size, ref rng); break;
                        case ClimbLook.Ledges:
                            Ledge(mb, local, normal, (Vector3)toLocal.MultiplyVector(Vector3.up), f.Color, size, !nested && n >= count / 3, ref rng);
                            nested |= n >= count / 3;
                            break;
                        case ClimbLook.Fur: Tuft(mb, local, normal, f.Color, size, ref rng); break;
                        default: Moss(mb, local, normal, f.Color, size, ref rng); break;
                    }
                    placed++;
                }
            }
            foreach (var kv in builders)
            {
                var (part, s) = kv.Key;
                var go = new GameObject($"Climb{sections[s].Look}");
                go.transform.SetParent(part.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = kv.Value.ToMesh($"{part.sharedMesh.name}Climb{s}");
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = StoneColossi.GlowMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.AddComponent<ColossusClimbDetail>().Part = part.GetComponent<MeshRenderer>();
            }
            Debug.Log($"ColossusClimbDetails: {root.name}: {placed} details on {sections.Length} sections, {builders.Count} meshes");
        }

        static Face Pick(List<Face> faces, float total, ref Unity.Mathematics.Random rng)
        {
            var at = rng.NextFloat(total);
            for (int i = 0; i < faces.Count; i++)
            {
                at -= faces[i].Area;
                if (at <= 0f)
                    return faces[i];
            }
            return faces[faces.Count - 1];
        }

        /// <summary>Two directions across a surface with this normal (unit).</summary>
        static void Across(Vector3 normal, out Vector3 u, out Vector3 v)
        {
            u = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            v = Vector3.Cross(normal, u);
        }

        static Color Shade(Color c, float by) => new Color(c.r * by, c.g * by, c.b * by, 1f);
        static readonly Color MossGreen = new Color(0.28f, 0.42f, 0.18f, 1f);
        static readonly Color VineGreen = new Color(0.22f, 0.36f, 0.14f, 1f);
        static readonly Color Twig = new Color(0.36f, 0.27f, 0.17f, 1f);

        /// <summary>A dark crack in the stone, a worn knob of a handhold beside it, and sometimes moss in it.</summary>
        static void Crack(DunesColossus.MeshBuilder mb, Vector3 at, Vector3 normal, Color stone, float size, ref Unity.Mathematics.Random rng)
        {
            Across(normal, out var u, out var v);
            var turn = rng.NextFloat(Mathf.PI);
            var along = u * Mathf.Cos(turn) + v * Mathf.Sin(turn);
            var length = rng.NextFloat(0.6f, 1.6f) * size;
            mb.Limb(at - along * length * 0.5f - normal * 0.02f * size, at + along * length * 0.5f - normal * 0.02f * size, 0.07f * size, 0.03f * size, Shade(stone, 0.35f), 5);
            var knob = at + Vector3.Cross(along, normal) * 0.3f * size + normal * 0.05f * size;
            mb.Ellipsoid(knob, new Vector3(0.16f, 0.1f, 0.16f) * size, Quaternion.LookRotation(normal, along), Shade(stone, 1.15f), 6, 3);
            if (rng.NextFloat() < 0.4f)
                mb.Ellipsoid(at + normal * 0.02f * size, new Vector3(0.18f, 0.05f, 0.12f) * size, Quaternion.LookRotation(along, normal), MossGreen, 6, 3);
        }

        /// <summary>A vine strand hanging down the surface from where it took root, with a few leaves.</summary>
        static void Vine(DunesColossus.MeshBuilder mb, Vector3 at, Vector3 normal, Vector3 down, Color stone, float size, ref Unity.Mathematics.Random rng)
        {
            // (Down the surface: gravity's way, flattened onto it.)
            var slope = (down - normal * Vector3.Dot(down, normal)).normalized;
            if (slope == Vector3.zero)
                slope = Vector3.Cross(normal, Vector3.right).normalized;
            var from = at + normal * 0.05f * size;
            var segments = rng.NextInt(3, 6);
            for (int i = 0; i < segments; i++)
            {
                var wander = Vector3.Cross(normal, slope) * rng.NextFloat(-0.25f, 0.25f);
                var to = from + (slope + wander).normalized * rng.NextFloat(0.6f, 1.1f) * size;
                mb.Limb(from, to, 0.05f * size, 0.04f * size, Color.Lerp(VineGreen, Twig, 0.25f), 4);
                var leaf = Vector3.Lerp(from, to, 0.5f) + Vector3.Cross(normal, slope) * (i % 2 == 0 ? 0.12f : -0.12f) * size + normal * 0.03f * size;
                mb.Ellipsoid(leaf, new Vector3(0.12f, 0.03f, 0.08f) * size, Quaternion.LookRotation(slope, normal), Color.Lerp(VineGreen, MossGreen, rng.NextFloat()), 5, 3);
                from = to;
            }
        }

        /// <summary>A worn ledge to shimmy along, a flat slab out of a steep face; one per section carries a bird nest
        /// with eggs.</summary>
        static void Ledge(DunesColossus.MeshBuilder mb, Vector3 at, Vector3 normal, Vector3 up, Color stone, float size, bool nest, ref Unity.Mathematics.Random rng)
        {
            var flat = (normal - up * Vector3.Dot(normal, up)).normalized;
            if (flat == Vector3.zero)
                flat = normal;
            var side = Vector3.Cross(up, flat).normalized;
            var length = rng.NextFloat(1.2f, 2.4f) * size;
            var depth = rng.NextFloat(0.3f, 0.45f) * size;
            var slab = at + flat * depth * 0.5f;
            mb.Ellipsoid(slab, new Vector3(length * 0.5f, 0.12f * size, depth), Quaternion.LookRotation(flat, up), Shade(stone, 1.08f), 8, 3);
            if (!nest)
                return;
            var bowl = slab + up * 0.12f * size;
            for (int i = 0; i < 7; i++)
            {
                var a = i * Mathf.PI * 2f / 7f;
                var b = a + Mathf.PI * 2f / 7f + 0.3f;
                var p = bowl + (side * Mathf.Cos(a) + flat * Mathf.Sin(a)) * 0.26f * size;
                var q = bowl + (side * Mathf.Cos(b) + flat * Mathf.Sin(b)) * 0.26f * size + up * 0.05f * size;
                mb.Limb(p, q, 0.035f * size, 0.03f * size, Twig, 4);
            }
            for (int e = 0; e < 3; e++)
                mb.Ellipsoid(bowl + (side * (e - 1) * 0.08f + flat * (e == 1 ? 0.05f : -0.03f)) * size + up * 0.05f * size,
                             new Vector3(0.05f, 0.065f, 0.05f) * size, Quaternion.identity, new Color(0.82f, 0.86f, 0.78f, 1f), 6, 4);
        }

        /// <summary>A tuft of fur: three shaggy cones out of the hide, leaning downhill.</summary>
        static void Tuft(DunesColossus.MeshBuilder mb, Vector3 at, Vector3 normal, Color hide, float size, ref Unity.Mathematics.Random rng)
        {
            Across(normal, out var u, out var v);
            var lean = Vector3.Cross(normal, u);
            for (int i = 0; i < 3; i++)
            {
                var foot = at + (u * rng.NextFloat(-0.15f, 0.15f) + v * rng.NextFloat(-0.15f, 0.15f)) * size;
                var tip = foot + (normal * 0.35f + lean * rng.NextFloat(0.25f, 0.5f) + u * rng.NextFloat(-0.1f, 0.1f)) * size;
                mb.Limb(foot - normal * 0.03f * size, tip, 0.09f * size, 0.015f * size, Shade(hide, rng.NextFloat(0.8f, 1.1f)), 4);
            }
        }

        /// <summary>A flat patch of moss on the surface.</summary>
        static void Moss(DunesColossus.MeshBuilder mb, Vector3 at, Vector3 normal, Color stone, float size, ref Unity.Mathematics.Random rng)
        {
            Across(normal, out var u, out _);
            var r = rng.NextFloat(0.3f, 0.7f) * size;
            mb.Ellipsoid(at, new Vector3(r, 0.06f * size, r * rng.NextFloat(0.6f, 1f)), Quaternion.LookRotation(u, normal),
                         Color.Lerp(MossGreen, stone, rng.NextFloat(0.1f, 0.35f)), 7, 3);
        }
    }

    /// <summary>
    /// A-107: one mesh of a colossus's climb details, drawn while it looks big enough on screen (DetailPolicy.ScreenSize
    /// of a detail's size, with hysteresis) and while its part draws its own mesh (a far level or a walking rig's skinned
    /// body stands in for the part: the details would float off it).
    /// </summary>
    public class ColossusClimbDetail : MonoBehaviour
    {
        /// <summary>A detail's size (m) and the reference pixels it has to look before it is drawn.</summary>
        const float Size = 0.4f, Pixels = 3f;

        public MeshRenderer Part;
        MeshRenderer own;
        bool shown = true;

        void Awake() => own = GetComponent<MeshRenderer>();

        void LateUpdate()
        {
            if (own == null)
                return;
            var view = DetailPolicy.Current.Data;
            var bounds = own.bounds;
            var distance = math.max(0f, math.distance((float3)bounds.center, view.Camera) - bounds.extents.magnitude);
            shown = (Part == null || Part.enabled) &&
                    DetailPolicy.Holds(DetailPolicy.ScreenSize(Size * math.max(view.Detail, 0.25f), distance, view.PixelsPerMetre), Pixels, shown);
            if (own.enabled != shown)
                own.enabled = shown;
        }
    }
}
