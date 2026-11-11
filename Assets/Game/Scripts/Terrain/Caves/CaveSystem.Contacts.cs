using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-135 row 4: where things stand on the cave's rock (cave plants, the rooms' set pieces, the dungeons' chests and
    /// stand-ins), as ContactShade footprints sorted into the chunks they darken. A chunk takes its list when it builds
    /// and MeshJob bakes the seam darkening into its vertices (through the rib shade, SeamRib); when a chunk's list
    /// changes (plant models grown, a room's kit made, a dungeon filled) it is rebuilt at its level, the old walls drawn
    /// until then. All of it is
    /// seed-derived (plants and rooms from the bake, the boxes from the dungeon's sockets), so every machine darkens
    /// alike; it only changes how the rock is drawn.
    /// </summary>
    public partial class CaveSystem
    {
        /// <summary>How far past a contact's own reach its chunks are listed (m): the skirt and apron hang a little out.</summary>
        const float ContactMargin = 4f;
        /// <summary>Rooms within this past LoadRadius have their set's contacts listed before their walls are near.</summary>
        const float RoomContactAhead = 40f;
        /// <summary>Rooms whose kits are made (for their contacts) per frame at most.</summary>
        const int RoomContactsPerFrame = 2;

        /// <summary>The contacts darkening each chunk (by key), and a hash of each list, which a build records.</summary>
        readonly Dictionary<int3, List<ContactShade.Contact>> contactBuckets = new Dictionary<int3, List<ContactShade.Contact>>();
        readonly Dictionary<int3, uint> contactHashes = new Dictionary<int3, uint>();
        /// <summary>The cave plants' contacts (null until their models are grown), the rooms' by room, and the dungeons'.</summary>
        List<ContactShade.Contact> plantContacts;
        readonly Dictionary<int, List<ContactShade.Contact>> roomContacts = new Dictionary<int, List<ContactShade.Contact>>();
        readonly List<ContactShade.Contact> boxContacts = new List<ContactShade.Contact>();
        /// <summary>Bumped whenever boxContacts changes, so a room rebuilds its tiles if the boxes in it did.</summary>
        int boxContactsVersion;
        int boxCount = -1;
        float nextBoxLookup;
        readonly Dictionary<World, EntityQuery> boxQueries = new Dictionary<World, EntityQuery>();
        bool contactsDirty;

        /// <summary>
        /// A wall vertex's rib shade (Data.w: Cave.shader multiplies its whole lit colour by 1 - 2.4 w, clamped to
        /// 0.25..1.3) with the seam darkening folded in. Not the rock tint: a floor's tone ignores that, and this way
        /// every look the wall takes (rock, floor, dressed stone, wood, iron) darkens alike.
        /// </summary>
        internal static float SeamRib(float rib, float shade)
        {
            var ribs = 1f - math.clamp(rib * 2.4f, -0.3f, 0.75f);
            return (1f - ribs * shade) / 2.4f;
        }

        /// <summary>The hash of a chunk's contact list (0: none), which a build records to tell when it's out of date.</summary>
        uint ChunkContactHash(int3 key) => contactHashes.TryGetValue(key, out var h) ? h : 0u;

        /// <summary>A chunk's contacts as a list its build owns (Persistent; empty when none).</summary>
        NativeArray<ContactShade.Contact> ChunkContacts(int3 key)
        {
            if (!contactBuckets.TryGetValue(key, out var list) || list.Count == 0)
                return new NativeArray<ContactShade.Contact>(0, Allocator.Persistent);
            var array = new NativeArray<ContactShade.Contact>(list.Count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < list.Count; i++)
                array[i] = list[i];
            return array;
        }

        /// <summary>
        /// Gathers what's new (plants once grown, rooms coming near, the dungeons' boxes now and then) and, if anything
        /// changed, sorts all of it into the chunks again. Cheap when nothing did.
        /// </summary>
        void RefreshContacts(in TerrainSource source, float3 camPos)
        {
            if (!source.Caves.IsCreated)
                return;
            ref var set = ref source.Caves.Value;

            if (plantContacts == null && plantsFrom != null && plantModels.Count >= plantSpecies.Count * PlantVariants)
            {
                plantContacts = new List<ContactShade.Contact>();
                for (int i = 0; i < set.Plants.Length; i++)
                    AddPlantContacts(set.Plants[i], plantContacts);
                contactsDirty = true;
            }

            var made = 0;
            var ahead = LoadRadius + RoomContactAhead;
            for (int i = 0; i < set.Rooms.Length && made < RoomContactsPerFrame; i++)
            {
                if (roomContacts.ContainsKey(i) || math.distance(set.Rooms[i].Centre, camPos) > ahead)
                    continue;
                roomContacts[i] = CaveRoomKit.Get(set.Rooms[i]).Contacts;
                made++;
                contactsDirty = true;
            }

            if (Time.unscaledTime >= nextBoxLookup)
            {
                nextBoxLookup = Time.unscaledTime + 1f;
                LookUpBoxes();
            }

            if (contactsDirty)
            {
                contactsDirty = false;
                SortContacts();
            }
        }

        /// <summary>A cave plant's footprint: its ground colliders and ground-standing boxes, else its stem.</summary>
        void AddPlantContacts(in CavePlant p, List<ContactShade.Contact> into)
        {
            var model = p.Species * PlantVariants + p.Variant;
            if (model < 0 || model >= plantModels.Count || p.Species >= speciesTorch.Count || speciesTorch[p.Species] > 0f)
                return;   // (a wall torch stands off the wall on a bracket: no seam)
            var m = plantModels[model];
            float c = math.cos(p.Yaw), s = math.sin(p.Yaw);
            float2 Turn(float2 v) => new float2(v.x * c - v.y * s, v.x * s + v.y * c);
            var before = into.Count;
            for (int k = 0; k < m.Colliders.Count; k++)
            {
                if (k < m.ColliderBase.Count && m.ColliderBase[k] > 0.05f)
                    continue;
                var col = m.Colliders[k];
                var at = Turn(new float2(col.x, col.y)) * p.Scale;
                into.Add(ContactShade.Contact.Disc(p.Position + new float3(at.x, 0f, at.y), col.z * p.Scale));
            }
            foreach (var box in m.Boxes)
            {
                var foot = box.Centre - math.mul(box.Rotation, new float3(0f, box.Half.y, 0f));
                if (foot.y > 0.15f)
                    continue;
                var at = Turn(foot.xz) * p.Scale;
                var axis = Turn(math.mul(box.Rotation, new float3(1f, 0f, 0f)).xz);
                into.Add(ContactShade.Contact.Box(p.Position + new float3(at.x, foot.y * p.Scale, at.y), axis, box.Half.xz * p.Scale));
            }
            if (into.Count > before)
                return;
            var radius = m.TrunkRadius > 0f ? m.TrunkRadius : math.min(m.Radius * 0.35f, 0.4f);
            if (radius > 0f)
                into.Add(ContactShade.Contact.Disc(p.Position, radius * p.Scale));
        }

        /// <summary>The dungeons' chest and stand-in boxes (DungeonPropBox, one list per world: the first found), by
        /// their foot. Only changes when a dungeon is filled.</summary>
        void LookUpBoxes()
        {
            var count = 0;
            DynamicBuffer<Punchy.Fauna.DungeonPropBox> found = default;
            foreach (var world in World.All)
            {
                if (!world.IsCreated)
                    continue;
                if (!boxQueries.TryGetValue(world, out var query))
                {
                    query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Punchy.Fauna.DungeonPropBox>());
                    boxQueries[world] = query;
                }
                if (query.CalculateEntityCount() != 1)
                    continue;
                var buffer = world.EntityManager.GetBuffer<Punchy.Fauna.DungeonPropBox>(query.GetSingletonEntity(), true);
                if (buffer.Length == 0)
                    continue;
                found = buffer;
                count = buffer.Length;
                break;
            }
            if (count == boxCount)
                return;
            boxCount = count;
            boxContacts.Clear();
            for (int i = 0; i < count; i++)
            {
                var b = found[i].Box;
                var foot = b.Centre - math.mul(b.Rotation, new float3(0f, b.Half.y, 0f));
                boxContacts.Add(ContactShade.Contact.Box(foot, math.mul(b.Rotation, new float3(1f, 0f, 0f)).xz, b.Half.xz));
            }
            boxContactsVersion++;
            contactsDirty = true;
        }

        /// <summary>Sorts every contact into the chunks it can darken and hashes each chunk's list.</summary>
        void SortContacts()
        {
            foreach (var list in contactBuckets.Values)
                list.Clear();
            if (plantContacts != null)
                foreach (var c in plantContacts)
                    Bucket(c);
            foreach (var list in roomContacts.Values)
                foreach (var c in list)
                    Bucket(c);
            foreach (var c in boxContacts)
                Bucket(c);
            contactHashes.Clear();
            foreach (var kv in contactBuckets)
            {
                if (kv.Value.Count == 0)
                    continue;
                var h = 2166136261u;
                foreach (var c in kv.Value)
                    h = math.hash(new float4(c.Base, c.Radius + c.Half.x * 3f + c.Half.y * 7f)) ^ (h * 16777619u);
                contactHashes[kv.Key] = h == 0u ? 1u : h;
            }
        }

        void Bucket(in ContactShade.Contact c)
        {
            if (chunkSize <= 0f)
                return;
            var reach = c.Extent + ContactMargin;
            var lo = (int3)math.floor((c.Base - reach) / chunkSize);
            var hi = (int3)math.floor((c.Base + reach) / chunkSize);
            for (int z = lo.z; z <= hi.z; z++)
            for (int y = lo.y; y <= hi.y; y++)
            for (int x = lo.x; x <= hi.x; x++)
            {
                var key = new int3(x, y, z);
                if (!contactBuckets.TryGetValue(key, out var list))
                    contactBuckets[key] = list = new List<ContactShade.Contact>();
                list.Add(c);
            }
        }

        /// <summary>The contacts a room's tiles are darkened by: its own set's and the dungeon boxes standing in it.</summary>
        List<ContactShade.Contact> RoomTileContacts(in CaveRoom room, CaveRoomKit.Kit kit)
        {
            var list = new List<ContactShade.Contact>(kit.Contacts);
            var centre = room.Centre;
            var reach = math.max(room.HX, room.HZ) + 2f;
            foreach (var c in boxContacts)
                if (math.abs(c.Base.y - room.Origin.y) < 2f && math.distance(c.Base.xz, centre.xz) < reach)
                    list.Add(c);
            return list;
        }

        void ReleaseContacts()
        {
            contactBuckets.Clear();
            contactHashes.Clear();
            plantContacts = null;
            roomContacts.Clear();
            boxContacts.Clear();
            boxCount = -1;
            boxContactsVersion++;
            boxQueries.Clear();
            contactsDirty = false;
        }
    }
}
