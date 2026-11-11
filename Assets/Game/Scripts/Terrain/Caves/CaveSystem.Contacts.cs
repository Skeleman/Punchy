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
    /// W-136 row 3: the footprints are ContactShade's (a plant's ModelFeet as on the terrain, a box's BoxFoot), they take
    /// the shared tunables, and the plants and boxes in a room darken its tiles as well as the rock.
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
        /// <summary>Bumped whenever the plants', the boxes' or the tunables' contacts change, so a room rebuilds its tiles
        /// if the contacts in it did.</summary>
        int tileContactsVersion;
        /// <summary>The ContactShade.Version the lists were sorted at.</summary>
        int contactShadeVersion = -1;
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
                tileContactsVersion++;
                contactsDirty = true;
            }
            if (contactShadeVersion != ContactShade.Version)
            {
                contactShadeVersion = ContactShade.Version;
                tileContactsVersion++;
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

        /// <summary>A cave plant's footprint: its feet as a terrain plant's (ContactShade.ModelFeet), else its stem.</summary>
        void AddPlantContacts(in CavePlant p, List<ContactShade.Contact> into)
        {
            var model = p.Species * PlantVariants + p.Variant;
            if (model < 0 || model >= plantModels.Count || p.Species >= speciesTorch.Count || speciesTorch[p.Species] > 0f)
                return;   // (a wall torch stands off the wall on a bracket: no seam)
            var first = into.Count;
            ContactShade.ModelFeet(plantModels[model], into, stem: true);
            for (int k = first; k < into.Count; k++)
                into[k] = into[k].Placed(p.Position, p.Yaw, p.Scale);
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
                boxContacts.Add(ContactShade.BoxFoot(b.Centre, b.Rotation, b.Half, ContactShade.Kind.Prop));
            }
            tileContactsVersion++;
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
                contactHashes[kv.Key] = ContactShade.Hash(kv.Value);
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

        /// <summary>The contacts a room's tiles are darkened by: its own set's (first, as the kit lists them) and the cave
        /// plants and dungeon boxes standing in it.</summary>
        List<ContactShade.Contact> RoomTileContacts(in CaveRoom room, CaveRoomKit.Kit kit)
        {
            var list = new List<ContactShade.Contact>(kit.Contacts);
            var centre = room.Centre;
            var reach = math.max(room.HX, room.HZ) + 2f;
            var floor = room.Origin.y;
            void Add(List<ContactShade.Contact> from)
            {
                if (from != null)
                    foreach (var c in from)
                        if (math.abs(c.Base.y - floor) < 2f && math.distance(c.Base.xz, centre.xz) < reach)
                            list.Add(c);
            }
            Add(plantContacts);
            Add(boxContacts);
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
            tileContactsVersion++;
            contactShadeVersion = -1;
            boxQueries.Clear();
            contactsDirty = false;
        }
    }
}
