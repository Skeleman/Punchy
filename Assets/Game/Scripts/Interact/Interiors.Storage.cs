using Punchy.Net;
using Punchy.Player;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Interact
{
    /// <summary>
    /// A-134 row 2: each house's own storage. The room (its walls, furniture and where its shelves and chests stand) is
    /// still one per house type, but what's in the storage is each door's: container id RoomBase + door * 4 + k (door:
    /// the Door anchor's index, k: the room's k-th store), filled from the room's seed, k and the door, so every machine
    /// fills it the same with nothing sent, and it belongs to that door's household (Ownership.OfContainer).
    /// Registered lazily, never dropped: the host registers the storage of every house within CullNear of any player,
    /// a joined client or a solo game that of houses near its own player, and anyone registers a door's on going in
    /// by it or on hearing from the host about it (Items.ReceiveContainer, for a chest the host changed before a client
    /// came near). So the count grows with the houses players have been near, not with the world, and what was taken
    /// stays taken. A player sees only the storage of the house they came in by (StorageHere), since every house of a
    /// type shares one room.
    /// </summary>
    public partial class Interiors
    {
        uint worldSeed;
        int storageCount;
        /// <summary>The doors whose storage is registered.</summary>
        readonly System.Collections.Generic.HashSet<int> storedDoors = new System.Collections.Generic.HashSet<int>();

        /// <summary>Whether a container id is room storage, and whose door's.</summary>
        public static bool StorageDoor(int id, out int door)
        {
            door = (id - Punchy.Items.Items.RoomBase) >> 2;
            return instance != null && id >= Punchy.Items.Items.RoomBase && door < instance.doorRoom.Length;
        }

        /// <summary>Whether the local player can reach container <paramref name="id"/> from where they are: room storage
        /// only in the house they came in by (every house of a type shares the room); anything else always.</summary>
        public static bool StorageHere(int id) =>
            !StorageDoor(id, out var door) || (door == instance.cameFrom && instance.inRoom >= 0 && instance.doorRoom[door] == instance.inRoom);

        /// <summary>Registers room-storage container <paramref name="id"/> (idempotent); false if it's not room storage
        /// or its room has no k-th store.</summary>
        public static bool RegisterStorage(int id)
        {
            if (!StorageDoor(id, out var door))
                return false;
            instance.RegisterStorageAt(door);
            var r = instance.doorRoom[door];
            return r >= 0 && ((id - Punchy.Items.Items.RoomBase) & 3) < (instance.rooms[r].Stores?.Count ?? 0);
        }

        /// <summary>Door <paramref name="door"/>'s storage, if its house has a room (idempotent).</summary>
        void RegisterStorageAt(int door)
        {
            if (door < 0 || door >= doorRoom.Length || doorRoom[door] < 0 || !storedDoors.Add(door))
                return;
            var r = doorRoom[door];
            var stores = rooms[r].Stores;
            if (stores == null)
                return;
            for (int k = 0; k < stores.Count; k++)
            {
                var id = Punchy.Items.Items.RoomBase + door * 4 + k;
                var seed = math.hash(new uint4(RoomSeed(worldSeed, r), (uint)k, (uint)door, 0x5E1Fu));
                Punchy.Items.Items.RegisterContainer(id, stores[k].front, stores[k].shelf ? "shelf" : "chest", seed);
                storageCount++;
                Debug.Log($"Interiors: door {door}'s {(stores[k].shelf ? "shelf" : "chest")} {id} (household {(Punchy.Fauna.Ownership.Known ? Punchy.Fauna.Ownership.OfContainer(id).ToString() : "not built yet")}): " +
                          $"{Punchy.Items.Items.DescribeContainer(id)}; {storageCount} room containers in all");
            }
        }

        /// <summary>Four times a second (with Cull): the storage of houses within CullNear of the players this machine
        /// answers for (the host: every player; otherwise its own).</summary>
        void RegisterNearStorage()
        {
            using var players = new NativeList<float2>(4, Allocator.Temp);
            if (Hosting)
            {
                using var q = server.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetPlayer>(), ComponentType.ReadOnly<LocalTransform>());
                using var ts = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                foreach (var t in ts)
                    players.Add(t.Position.xz);
            }
            if (TryPlayer(out _, out var local))
                players.Add(local.Position.xz);
            if (players.Length == 0)
                return;
            for (int d = 0; d < doorRoom.Length; d++)
            {
                if (doorRoom[d] < 0 || rooms[doorRoom[d]].Stores == null || rooms[doorRoom[d]].Stores.Count == 0 ||
                    storedDoors.Contains(d) || !Doors.TryGetDoor(d, out var at, out _))
                    continue;
                foreach (var p in players)
                    if (math.distancesq(at.xz, p) < CullNear * CullNear)
                    {
                        RegisterStorageAt(d);
                        break;
                    }
            }
        }
    }
}
