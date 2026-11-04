using Punchy.Dungeon;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// A stand-in cave dungeon (#24), only with -dungeontest: one 24-socket ring on the hut cave floor with NearWater
    /// sockets, because the sunken temple (TempleSockets) has no pools yet, so drinking at NearWater can be tried solo,
    /// as host and as a client. Every world makes the same sockets from the world seed, as the set pieces do. Delete
    /// this once a real set piece writes NearWater sockets (the temple's pools wait for a world-lane tile).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DungeonSpawnSystem))]
    public partial class DungeonStandInSystem : SystemBase
    {
        /// <summary>The hut cave floor (-goto 530,45,958).</summary>
        static readonly float3 Centre = new(530f, 45f, 958f);

        protected override void OnCreate()
        {
            RequireForUpdate<TerrainSource>();
            Enabled = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-dungeontest") >= 0;
        }

        protected override void OnUpdate()
        {
            Enabled = false;
            var source = SystemAPI.GetSingleton<TerrainSource>();
            // (Dungeon ids from 0x10000 up, clear of the temple's, which is its landmark index.)
            Make(source, 0x10000, DungeonSpawner.Cave, new float3(0, 0, 0));
        }

        void Make(in TerrainSource source, uint dungeonId, ushort type, float3 offset)
        {
            var e = EntityManager.CreateEntity();
            EntityManager.SetName(e, $"Stand-in dungeon {dungeonId}");
            var buffer = EntityManager.AddBuffer<DungeonSocket>(e);
            for (int i = 0; i < 24; i++)
            {
                // A ring of 12 creature sockets, with chests, wall decor, interactables and clutter between them.
                var kind = i < 12 ? SocketKind.Creature : (SocketKind)(1 + (i - 12) % 4);
                var angle = i * (math.PI * 2f / 12f) + (i < 12 ? 0f : 0.26f);
                var radius = i < 12 ? 3.5f : 5.5f;
                var p = Centre + offset + new float3(math.sin(angle) * radius, 2f, math.cos(angle) * radius);
                p.y = source.FloorUnder(p, out _);
                var tags = SocketTags.Dark;
                if (i % 4 == 1)
                    tags |= SocketTags.NearWater;
                if (i % 4 == 3)
                    tags |= SocketTags.NearChest;
                buffer = EntityManager.GetBuffer<DungeonSocket>(e);
                buffer.Add(new DungeonSocket
                {
                    Pos = p,
                    Rot = quaternion.RotateY(angle + math.PI),
                    Kind = kind,
                    Size = (byte)(i % 3),
                    Tags = (uint)tags,
                    Seed = DungeonSocket.MakeSeed(source.World.Seed, dungeonId, (uint)i),
                    DungeonType = type,
                    Biome = 0,
                });
            }
        }
    }
}
