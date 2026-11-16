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
            !StorageDoor(id, out var door) ||
            (instance.walkInOf.TryGetValue(door, out var house) ? instance.walkInAt == house   // (P-37 r3: inside its walk-in house)
                : door == instance.cameFrom && instance.inRoom >= 0 && instance.doorRoom[door] == instance.inRoom);

        /// <summary>The room whose stores door <paramref name="door"/>'s storage is seeded from: its own room, or a walk-in
        /// house's type's room (P-37 r3); -1 if neither.</summary>
        int StorageRoom(int door) =>
            door < 0 || door >= doorRoom.Length ? -1 : doorRoom[door] >= 0 ? doorRoom[door] : walkInOf.TryGetValue(door, out var w) ? walkIns[w].Room : -1;

        /// <summary>Registers room-storage container <paramref name="id"/> (idempotent); false if it's not room storage
        /// or its room has no k-th store.</summary>
        public static bool RegisterStorage(int id)
        {
            if (!StorageDoor(id, out var door))
                return false;
            instance.RegisterStorageAt(door);
            // (A walk-in house's k >= 1 stores were merged into k = 0 when registered: only what's left counts.)
            return Punchy.Items.Items.HasContainer(id);
        }

        /// <summary>Door <paramref name="door"/>'s storage, if its house has a room (idempotent).</summary>
        void RegisterStorageAt(int door)
        {
            var r = StorageRoom(door);
            if (r < 0 || !storedDoors.Add(door))
                return;
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
            if (walkInOf.TryGetValue(door, out var house) && stores.Count > 0)
                MergeIntoDoorContainer(door, walkIns[house], stores.Count);
        }

        /// <summary>
        /// P-37 r3 (#369): a walk-in house has no room for its stores to stand in, so they become one container for its
        /// door, just inside the doorway: k = 0 moves there, and the other stores' stacks join it (the same on every
        /// machine, straight after seeding, so nothing is sent; containers aren't saved, so this is the "on load" move).
        /// Stacks that don't fit keep their own container, at the same spot.
        /// </summary>
        void MergeIntoDoorContainer(int door, in WalkIn house, int storeCount)
        {
            var into = Punchy.Items.Items.RoomBase + door * 4;
            var at = WalkInChestAt(house);
            Punchy.Items.Items.MoveContainer(into, at, "chest");
            for (int k = 1; k < storeCount; k++)
                if (!Punchy.Items.Items.MergeContainer(into + k, into))
                    Punchy.Items.Items.MoveContainer(into + k, at);
            DrawWalkInChest(at, house.Facing);
            Debug.Log($"Interiors: walk-in door {door}'s chest {into} at {at}: {Punchy.Items.Items.DescribeContainer(into)} ({storeCount} room stores joined)");
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
                var r = StorageRoom(d);
                if (r < 0 || rooms[r].Stores == null || rooms[r].Stores.Count == 0 ||
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
