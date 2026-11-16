using Punchy.Interact;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// Who owns a thing (A-134 row 2, DESIGN-npc-ownership): a household (its index in Households.All) or Nobody. It is
    /// derived, never sent or stored: the storage in a house's room belongs to the household whose door leads into it
    /// (by the door's spot, as the households themselves are placed), so every machine names the same owner. Wild
    /// items, drops, loot, temple chests and the folk's spares belong to nobody (spares are how players try fishing, so
    /// taking one is never theft). Every take path and the Use prompt ask this one place; Take is where row 3's
    /// witnesses and thefts hook in.
    /// </summary>
    public static class Ownership
    {
        public const int Nobody = -1;
        /// <summary>Whether owners can be named yet: the households are built a few seconds into a game, and until
        /// then every owner reads Nobody.</summary>
        public static bool Known => Households.Ready;
        /// <summary>How near (DoorKey cells of half a metre, along each axis) a Door anchor is to a household's door to
        /// be that house's: the households stand their doors where AnchorBaking does, so they agree to rounding.</summary>
        internal const int DoorCells = 3;

        /// <summary>The household whose door stands at a spot (within DoorCells), or Nobody.</summary>
        public static int HouseAt(float2 xz)
        {
            var q = (int2)math.round(xz * 2f);
            for (int dz = -DoorCells; dz <= DoorCells; dz++)
                for (int dx = -DoorCells; dx <= DoorCells; dx++)
                    if (Households.ByDoor.TryGetValue(q + new int2(dx, dz), out var house))
                        return house;
            return Nobody;
        }

        /// <summary>A container's owner: room storage is its door's household's; anything else (a temple chest) nobody's.</summary>
        public static int OfContainer(int id) =>
            Interiors.StorageDoor(id, out var door) && Doors.TryGetDoor(door, out var at, out _) ? HouseAt(at.xz) : Nobody;

        /// <summary>A loose item's owner (by uid). Every item lying in the world is nobody's for now: wild finds, drops,
        /// loot and spares. A house's things are in its storage (OfContainer); marking what's in a bag is P-35's.</summary>
        public static int OfItem(int uid) => Nobody;

        /// <summary>How a prompt names an owned thing: "Alba's chest", or "the chest" when it's nobody's.</summary>
        public static string Whose(int owner, string thing)
        {
            if (owner < 0 || owner >= Households.All.Count)
                return "the " + thing;
            var house = Households.All[owner];
            var head = 0;
            for (int m = 0; m < house.Members.Length; m++)
                if (house.Members[m].Role == Kin.Head)
                {
                    head = m;
                    break;
                }
            return $"{house.NameOf(head)}'s {thing}";
        }

        /// <summary>
        /// A take, on the machine that decides it (the host, or a solo game): <paramref name="player"/> is the taker's
        /// NetworkId, or -1 for the local player. Owned takes are logged here; row 3 hands them on to the witnesses.
        /// </summary>
        public static void Took(int owner, int player, string what)
        {
            if (owner == Nobody)
                return;
            UnityEngine.Debug.Log($"[ownership] {(player < 0 ? "the local player" : $"player {player}")} took {what} from household {owner} ({Whose(owner, "house")})");
        }
    }
}
