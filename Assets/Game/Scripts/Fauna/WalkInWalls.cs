using System.Collections.Generic;
using Punchy.Flora;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// P-37 r3 row 2 (#369): the walls of walk-in houses, for creatures. A walk-in house (StructureKit.WalkIn) has no
    /// wall circles: its kit boxes are its walls and its door leaf is a box that is solid only while the door is shut.
    /// Creatures tested walls by those circles (FloraPlacement.InsideWalls), so they walked through walk-in houses.
    /// This holds each such model's boxes (model space: walls, then door leaves) and the doors open this frame, and
    /// <see cref="Inside"/> tests a body against them as <see cref="BuildingFootprints"/> places the house. Only boxes
    /// that cross the body band (<see cref="BandLow"/> to <see cref="BandHigh"/> over the house's base) count, so floors,
    /// steps and roofs don't. A creature passes a doorway only while its door is open (FloraDoors, half way or more).
    /// Read from the seed's models and the doors this machine already shows: nothing is sent.
    /// </summary>
    public struct WalkInWalls : IComponentData
    {
        /// <summary>Per flora model (placement's model index): x first box, y wall boxes, z door boxes. Empty: none.</summary>
        [ReadOnly] public NativeArray<int4> Models;
        /// <summary>Every walk-in model's boxes, model space (KitCollision.Placed turns one into the world).</summary>
        [ReadOnly] public NativeArray<KitBox> Boxes;
        /// <summary>Doors open half way or more (x, z), the first <see cref="OpenCount"/> of them.</summary>
        [ReadOnly] public NativeArray<float2> OpenDoors;
        public int OpenCount;

        /// <summary>The body band over the house's base (m): a box must reach into it to stop a creature.</summary>
        public const float BandLow = 0.4f, BandHigh = 1.4f;
        /// <summary>How far from a door leaf an opened door still counts (a door is opened by its anchor, just out from
        /// the leaf; FloraSystem's DoorReach).</summary>
        public const float DoorReach = 2.5f;

        /// <summary>Whether there are any walk-in houses at all (none on a world without them: no cost then).</summary>
        public bool Any => Boxes.IsCreated && Boxes.Length > 0;

        /// <summary>Whether this model is a walk-in house.</summary>
        public bool Is(int model) => Any && model >= 0 && model < Models.Length && Models[model].y > 0;

        /// <summary>
        /// Whether a body of this radius at xz is in a walk-in house's walls, or in its doorway while the door is shut.
        /// The house as placed (BuildingFootprints' plot placement: position, yaw and scale as FloraSystem draws it).
        /// </summary>
        public bool Inside(in FloraSystem.FloraInstance plant, float2 xz, float radius)
        {
            if (!Is(plant.Model))
                return false;
            var m = Models[plant.Model];
            var scale = math.max(plant.Scale, 1e-3f);
            float c = math.cos(plant.Yaw), sn = math.sin(plant.Yaw);
            // Into model space (the inverse of KitCollision.Placed's turn).
            var d = (xz - plant.Position.xz) / scale;
            var local = new float2(d.x * c + d.y * sn, -d.x * sn + d.y * c);
            var r = radius / scale;
            var low = BandLow / scale;
            var high = BandHigh / scale;
            for (int i = m.x; i < m.x + m.y + m.z; i++)
            {
                var b = Boxes[i];
                if (math.distancesq(local, b.Centre.xz) > math.square(b.Reach + r) || !Crosses(b, local, r, low, high))
                    continue;
                if (i >= m.x + m.y)
                {
                    // A door leaf: in the way only while its door is shut.
                    var p = b.Centre;
                    var world = plant.Position.xz + new float2(p.x * c - p.z * sn, p.x * sn + p.z * c) * scale;
                    if (DoorOpen(world))
                        continue;
                }
                return true;
            }
            return false;
        }

        /// <summary>Whether a door is open near a leaf at this spot.</summary>
        public bool DoorOpen(float2 leaf)
        {
            for (int k = 0; k < OpenCount && k < OpenDoors.Length; k++)
                if (math.distancesq(OpenDoors[k], leaf) < DoorReach * DoorReach)
                    return true;
            return false;
        }

        /// <summary>
        /// Whether a box (model space) reaches a body of radius r at p between heights low and high: the point taken into
        /// the box's frame at the band's middle, grown by the radius across and by half the band up and down (exact for
        /// upright walls; a tilted roof or stair is near enough).
        /// </summary>
        public static bool Crosses(in KitBox b, float2 p, float r, float low, float high)
        {
            var mid = (low + high) * 0.5f;
            var half = (high - low) * 0.5f;
            var o = math.mul(math.conjugate(b.Rotation), new float3(p.x, mid, p.y) - b.Centre);
            return math.abs(o.x) < b.Half.x + r && math.abs(o.z) < b.Half.z + r && math.abs(o.y) < b.Half.y + half;
        }

        /// <summary>An empty set (no walk-in houses), for a job before the system has made its own.</summary>
        public static WalkInWalls Empty(Allocator allocator) => new WalkInWalls
        {
            Models = new NativeArray<int4>(0, allocator),
            Boxes = new NativeArray<KitBox>(0, allocator),
            OpenDoors = new NativeArray<float2>(0, allocator)
        };

        public void Dispose()
        {
            if (Models.IsCreated) Models.Dispose();
            if (Boxes.IsCreated) Boxes.Dispose();
            if (OpenDoors.IsCreated) OpenDoors.Dispose();
        }
    }

    /// <summary>
    /// P-37 r3 row 2 (#369): keeps <see cref="WalkInWalls"/> where animals are simulated: the walk-in models' boxes
    /// (again whenever FloraSystem's models change) and, every frame, the doors open on this machine. A walk-in model is
    /// a building whose kit boxes are its only walls (no wall circles nor trunk), which is what StructureKit.WalkIn makes.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(CreatureBrainSystem))]
    public partial class WalkInWallsSystem : SystemBase
    {
        FloraSystem flora;
        float nextLook;
        int builtModels = -1;
        Entity entity;
        WalkInWalls walls;

        protected override void OnCreate()
        {
            walls = WalkInWalls.Empty(Allocator.Persistent);
            walls.OpenDoors.Dispose();
            walls.OpenDoors = new NativeArray<float2>(FloraDoors.Max, Allocator.Persistent);
            entity = EntityManager.CreateEntity();
            EntityManager.SetName(entity, "WalkInWalls");
            EntityManager.AddComponentData(entity, walls);
        }

        protected override void OnDestroy()
        {
            EntityManager.CompleteAllTrackedJobs();
            walls.Dispose();
        }

        protected override void OnUpdate()
        {
            if (flora == null && UnityEngine.Time.unscaledTime >= nextLook)
            {
                nextLook = UnityEngine.Time.unscaledTime + 0.5f;
                flora = Object.FindAnyObjectByType<FloraSystem>();
            }
            // (Writing the singleton waits for last frame's brain, which reads it.)
            ref var w = ref SystemAPI.GetSingletonRW<WalkInWalls>().ValueRW;
            var count = flora != null && flora.Built ? flora.ModelCount : 0;
            if (count != builtModels)
            {
                builtModels = count;
                EntityManager.CompleteAllTrackedJobs();
                Rebuild(count);
                Debug.Log($"[walkin] {WalkInModels()} walk-in house models, {walls.Boxes.Length} wall and door boxes, for creatures");
            }
            walls.OpenCount = FloraDoors.OpenSnapshot(walls.OpenDoors);
            w = walls;
        }

        int WalkInModels()
        {
            var n = 0;
            for (int i = 0; i < walls.Models.Length; i++)
                n += walls.Models[i].y > 0 ? 1 : 0;
            return n;
        }

        void Rebuild(int count)
        {
            var models = new List<int4>();
            var boxes = new List<KitBox>();
            for (int i = 0; i < count; i++)
            {
                var model = flora.ModelAt(i);
                if (model == null || !WalksIn(model))
                {
                    models.Add(int4.zero);
                    continue;
                }
                var first = boxes.Count;
                boxes.AddRange(model.Boxes);
                var doors = DoorsOf(model);
                boxes.AddRange(doors);
                models.Add(new int4(first, model.Boxes.Count, doors.Count, 0));
            }
            if (boxes.Count == 0)
                models.Clear();   // (Any is false then, and Is never looks.)
            walls.Models.Dispose();
            walls.Boxes.Dispose();
            walls.Models = new NativeArray<int4>(models.ToArray(), Allocator.Persistent);
            walls.Boxes = new NativeArray<KitBox>(boxes.ToArray(), Allocator.Persistent);
        }

        /// <summary>A building whose kit boxes are its only walls: a walk-in house (StructureKit.WalkIn drops its circles).</summary>
        static bool WalksIn(FloraModel model) =>
            model.Species != null && model.Species.Form == FloraForm.KitBuilding && model.Boxes.Count > 0 &&
            model.Colliders.Count == 0 && model.TrunkRadius <= 0f;

        /// <summary>Its door leaves (model space), from the walk-in house itself (P-37 r3 row 1).</summary>
        static List<KitBox> DoorsOf(FloraModel model) => model.DoorBoxes;
    }
}
