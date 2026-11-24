using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.CameraRig
{
    /// <summary>
    /// The camera's open-air ground checks in Burst, as the cave arm's are (ArmProbe, #54): whether the head is
    /// underground, whether the lens sits in rock's air, the ground under the lens, the slide along the ground when
    /// looking up and the hill sight line (16 samples). As managed TerrainSource calls they were about 22 a frame, ~1 ms
    /// of main thread (q183). Same steps in the same order; "-oldview" runs the managed code as before.
    /// </summary>
    public partial class ThirdPersonCamera
    {
        /// <summary>A/B (-oldview): the open-air ground checks in managed code, as before.</summary>
        public static bool OldView;

        /// <summary>Whether the head is underground: by a cave, in a temple room or a house's room, or under the surface.</summary>
        static bool HeadUnder(in TerrainSource terrain, float3 head)
        {
            var result = new NativeArray<int>(1, Allocator.TempJob);
            Unity.Jobs.IJobExtensions.Run(new HeadProbe { Terrain = terrain, Head = head, Result = result });
            var under = result[0] != 0;
            result.Dispose();
            return under;
        }

        /// <summary>
        /// After the cave arm: keeps the lens above the ground (and any roof, rock or rim it sank into) and out of a hill
        /// between it and the player, as UpdateView's managed checks do. Leaves the lens alone in rock's air.
        /// </summary>
        void OpenAir(in TerrainSource terrain, ref Vector3 position, ref Quaternion rotation, Vector3 target, float orbitPitch)
        {
            var lens = new NativeArray<float>(2, Allocator.TempJob);
            Unity.Jobs.IJobExtensions.Run(new LensProbe
            {
                Terrain = terrain, Lens = ToFloat3(position), Underground = underground, Clearance = GroundClearance, Result = lens
            });
            var inRock = lens[0] != 0f;
            var minY = lens[1];
            lens.Dispose();
            if (inRock)
                return;
            var groundMinY = minY;
            // Roofs, rocks and rims (PropSupport): stay above one the camera has sunk into, not ones overhead.
            if (!supportQuery.IsEmpty)
            {
                var holder = supportQuery.GetSingletonEntity();
                var em = World.DefaultGameObjectInjectionWorld.EntityManager;
                var grids = em.GetComponentData<Punchy.Flora.SupportGrids>(holder);
                if (grids.Set.IsCreated && Punchy.Flora.PropSupport.Sample(ref grids.Set.Value, em.GetBuffer<Punchy.Flora.FloraSupport>(holder, true),
                        new float3(position.x, position.y, position.z), 0.2f, 0.5f, out var top))
                    minY = Mathf.Max(minY, top.Height + GroundClearance);
            }
            var result = new NativeArray<float3>(3, Allocator.TempJob);
            Unity.Jobs.IJobExtensions.Run(new GroundProbe
            {
                Terrain = terrain, Position = ToFloat3(position), Target = ToFloat3(target), MinY = minY, GroundMinY = groundMinY,
                OrbitPitch = orbitPitch, Clearance = GroundClearance, Underground = underground, Result = result
            });
            position = result[0];
            if (result[1].x != 0f)
                rotation = Quaternion.LookRotation(target - (Vector3)result[2]);
            result.Dispose();
        }

        [BurstCompile]
        struct HeadProbe : Unity.Jobs.IJob
        {
            public TerrainSource Terrain;
            public float3 Head;
            public NativeArray<int> Result;

            public void Execute()
            {
                var ceilingUp = Head.y < Terrain.SampleHeight(Head.xz) - 0.3f;
                // (P-13: a house's room, off the island: InteriorCells boxes are solid to SolidDistance too.)
                Result[0] = Terrain.CaveDistance(Head) < CaveNear || Terrain.TempleAir(Head) || ceilingUp ||
                            InteriorCells.Distance(Head) < CaveNear ? 1 : 0;
            }
        }

        /// <summary>Result[0] 1 when the lens is in rock's air (placed as it is), else Result[1] the ground's height plus clearance.</summary>
        [BurstCompile]
        struct LensProbe : Unity.Jobs.IJob
        {
            public TerrainSource Terrain;
            public float3 Lens;
            public bool Underground;
            public float Clearance;
            public NativeArray<float> Result;

            public void Execute()
            {
                // (P-19: the lens may go under cave water now: Underwater draws it. P-18 kept it above.)
                if (Terrain.CaveDistance(Lens) < 0f || Underground && (Terrain.TempleAir(Lens) || Lens.y < Terrain.SampleHeight(Lens.xz)))
                {
                    Result[0] = 1f;
                    return;
                }
                Result[0] = 0f;
                // (P-19: the ground only, not water: the lens may go under a lake, a river or the sea, and Underwater draws it.)
                Result[1] = Terrain.SampleGround(Lens.xz).Height + Clearance;
            }
        }

        /// <summary>Result[0] the lens's position; Result[1].x 1 when it rose and looks back at the pivot from Result[2].</summary>
        [BurstCompile]
        struct GroundProbe : Unity.Jobs.IJob
        {
            public TerrainSource Terrain;
            public float3 Position, Target;
            public float MinY, GroundMinY, OrbitPitch, Clearance;
            public bool Underground;
            public NativeArray<float3> Result;

            public void Execute()
            {
                var position = Position;
                var target = Target;
                var lookAt = false;
                var lookFrom = float3.zero;
                if (position.y < MinY)
                {
                    // P-20 (#85, item 26): looking up, the camera slides along the ground in towards the character with its
                    // view still pitched up; only when that leaves less than MinSlideArm does it rise and look down at the head.
                    var slid = false;
                    // A-102 (row 3b): a diving body's camera slides in along its arm the same way and stays under, with the diver.
                    var pivotWater = Terrain.SampleGround(target.xz);
                    var diving = pivotWater.HasWater && target.y < pivotWater.WaterSurface && target.y > pivotWater.Height + Clearance;
                    // (Only against bare ground: over a roof, rock or rim the ground height says nothing about the prop.)
                    if ((OrbitPitch < 0f && target.y > MinY || diving) && MinY <= GroundMinY + 0.01f)
                    {
                        var arm = position - target;
                        float lo = 0f, hi = 1f;
                        for (var i = 0; i < 10; i++)
                        {
                            var mid = (lo + hi) * 0.5f;
                            var p = target + arm * mid;
                            if (p.y >= Terrain.SampleGround(p.xz).Height + Clearance)
                                lo = mid;
                            else
                                hi = mid;
                        }
                        var slide = target + arm * lo;
                        var under = Terrain.SampleGround(slide.xz).Height + Clearance;
                        if (lo * math.length(arm) >= MinSlideArm && slide.y >= under - 0.01f)
                        {
                            position = slide;
                            slid = true;
                        }
                    }
                    if (!slid)
                    {
                        position.y = MinY;
                        lookAt = true;
                        lookFrom = position;
                    }
                }
                // A hill between the player and the camera (A-37): the camera comes in along its line to just before
                // the ground. Not near caves, where the ground height says nothing about the tunnel the player is in.
                if (!Underground)
                {
                    var arm = position - target;
                    var armLength = math.length(arm);
                    for (var i = 1; i <= SightSamples; i++)
                    {
                        var p = target + arm * ((float)i / SightSamples);
                        if (p.y >= Terrain.SampleHeight(p.xz) + Clearance * 0.5f)
                            continue;
                        position = target + arm * math.max((i - 1f) / SightSamples, math.min(1f, MinSightArm / math.max(armLength, 1e-4f)));
                        break;
                    }
                }
                Result[0] = position;
                Result[1] = new float3(lookAt ? 1f : 0f, 0f, 0f);
                Result[2] = lookFrom;
            }
        }
    }
}
