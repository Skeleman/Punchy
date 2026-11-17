#if UNITY_EDITOR
using Punchy.Fauna;
using Punchy.Flora;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Debugging
{
    /// <summary>
    /// Test hooks for creatures and walk-in houses (P-37 r3 row 2, #369): a walk-in house's kit boxes are walls to a
    /// creature, its doorway is shut to one unless its door is open, and floors and roofs don't stop anything.
    /// </summary>
    public static partial class TestHooks
    {
        /// <summary>
        /// A made-up walk-in house (4 x 4 m room, walls 0.2 m thick and 2.4 m high, a door leaf in the front wall, a
        /// floor slab and a roof), turned and scaled, asked where a creature would be stopped. Empty: every answer right;
        /// else each wrong one, "; "-separated. Logs how many real walk-in models WalkInWallsSystem found.
        /// </summary>
        public static string WalkInWallProblems()
        {
            var problems = "";
            var walls = new WalkInWalls
            {
                Models = new NativeArray<int4>(new[] { int4.zero, new int4(0, 6, 1, 0) }, Allocator.Temp),
                Boxes = new NativeArray<KitBox>(new[]
                {
                    Box(new float3(0f, 1.2f, -2f), new float3(4f, 2.4f, 0.2f)),     // back wall
                    Box(new float3(-2f, 1.2f, 0f), new float3(0.2f, 2.4f, 4f)),     // left wall
                    Box(new float3(2f, 1.2f, 0f), new float3(0.2f, 2.4f, 4f)),      // right wall
                    Box(new float3(-1.25f, 1.2f, 2f), new float3(1.5f, 2.4f, 0.2f)),// front wall, left of the door
                    Box(new float3(1.25f, 1.2f, 2f), new float3(1.5f, 2.4f, 0.2f)), // front wall, right of the door
                    Box(new float3(0f, 0.1f, 0f), new float3(4f, 0.2f, 4f)),        // floor slab (under the band)
                    Box(new float3(0f, 0.6f, 2.05f), new float3(1f, 1.2f, 0.1f))    // door leaf (shut: in the way)
                }, Allocator.Temp),
                OpenDoors = new NativeArray<float2>(1, Allocator.Temp)
            };
            var plant = new FloraSystem.FloraInstance { Position = new float3(100f, 5f, 50f), Yaw = math.radians(30f), Scale = 1.1f, Model = 1 };
            float2 At(float x, float z)
            {
                // A spot in the house's own frame, into the world (as KitCollision.Placed turns it).
                float c = math.cos(plant.Yaw), s = math.sin(plant.Yaw);
                return plant.Position.xz + new float2(x * c - z * s, x * s + z * c) * plant.Scale;
            }
            void Expect(string what, float2 xz, float radius, bool blocked)
            {
                if (walls.Inside(plant, xz, radius) != blocked)
                    problems += $"{what}: {(blocked ? "not stopped" : "stopped")}; ";
            }
            Expect("in the back wall", At(0f, -2f), 0.3f, true);
            Expect("against the left wall", At(-1.75f, 0f), 0.3f, true);
            Expect("room centre", At(0f, 0f), 0.4f, false);
            Expect("outside", At(5f, 5f), 0.4f, false);
            Expect("shut doorway", At(0f, 2f), 0.3f, true);
            walls.OpenDoors[0] = At(0f, 2.6f);
            walls.OpenCount = 1;
            Expect("open doorway", At(0f, 2f), 0.3f, false);
            Expect("beside the open door", At(1.25f, 2f), 0.3f, true);
            plant.Model = 0;
            Expect("not a walk-in model", At(0f, -2f), 0.3f, false);
            walls.Dispose();

            var director = Object.FindAnyObjectByType<FaunaDirector>();
            if (director != null && director.SimWorld != null && director.SimWorld.IsCreated)
            {
                using var query = director.SimWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<WalkInWalls>());
                if (query.CalculateEntityCount() != 1)
                    problems += "no WalkInWalls where animals live; ";
                else
                {
                    var real = query.GetSingleton<WalkInWalls>();
                    var models = 0;
                    for (int i = 0; i < real.Models.Length; i++)
                        models += real.Models[i].y > 0 ? 1 : 0;
                    Debug.Log($"[walkin test] {models} walk-in models, {real.Boxes.Length} boxes, {real.OpenCount} doors open");
                }
            }
            return problems;
        }

        static KitBox Box(float3 centre, float3 size) =>
            new KitBox { Centre = centre, Half = size * 0.5f, Rotation = quaternion.identity, Reach = math.length(size) * 0.5f };
    }
}
#endif
