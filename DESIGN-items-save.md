# Items, worn slots, ownership and the saved world (P-35 row 1, #26)

Status: design note for review, no code. Written by the player lane on 11-13 from main da573e16, plus the open
`player/unify-items` and `player/take-items` branches (folk kits, two hands, spares at posts). It serves foundation (3) of
"What we are building": one item and save model that later features build on instead of inventing their own stand-in.
The open questions for Zach are listed at the end. Rule 27 holds until they are answered.

## What this settles

1. **Item definition:** one definition of what an item is, with a stable id, and what of it goes over the network.
2. **Kit:** one set of hands, worn slots and bag, the same for players and folk.
3. **Ownership:** who owns a thing or a house, and how an item remembers it after it is moved.
4. **Saved world:** what it holds beyond the seed, and which machine keeps and sends each part.
5. **Stand-ins:** each one in the code today, and how it moves over.
6. **Network table changes:** every one this needs, listed for a single rule 11 approval.

## 1. Where things stand

| State | Where it lives now | Who decides | How it is sent | Saved? |
|---|---|---|---|---|
| Item kinds | `ItemTable.Items`, a C# array; the id is the index (byte); 75 rows on take-items | code | ids as bytes in every RPC | n/a |
| Player inventory | `Items.slots` on each machine, 16 + pack slots | the owner's machine, after a host grant (GrantRpc) | not sent | no |
| Player hands and wear | `GearSet` (right, left hand, head, back, body, legs, feet, neck, sheathed, block, hair) | the owner's machine | GearRpc + ArmourRpc + SlotRpc to the host, which relays the matching State RPCs | no |
| Eyewear (A-103 row 5) | `NetPlayerLook.Eyewear`, toggled with the G key | the owner's machine | ghost field | no |
| Folk kit | `FolkKit` in CreatureState: 13 bytes (id+1), including Post0/Post1 props | the host (FolkKitSystem, from the brain's `HeldItem` and job) | CreatureNet snapshot | no (derived again) |
| World items | `Items.world`: seeded door items (door*4+k), chest items (1M+), drops (2M+, up to 256), spares (3M+) | the host | ItemSpawnRpc / ItemGoneRpc, catch-up for late joiners | no |
| Containers | chests (socket index), room storage (100k + room*4+k), filled from the seed | the host after the first change | ContainerSlotRpc | no |
| Spares at posts | host map: spare uid to worker and post | the host | as world items | no |
| Households | derived from the house's placement hash (A-105) | every machine alike | nothing | no |
| Dead household members | `Household.Lost`, on the host; a client marks the ones it saw go (WatchKinLost) | the host | not sent | no |
| Quests | `quests-save.json` on each machine | each player | a joined client tells the host what it had taken | yes, per machine |
| Stats | PlayerPrefs (PlayerStats) | the owner | | yes, per machine |
| Body, coat, hair, player Individual | PlayerPrefs | the owner | NetPlayerLook / SlotRpc | yes, per machine |
| Doors and lamps | Doors / Interiors, per session | the host | DoorStateRpc, LampStateRpc | no |
| Dungeon chests opened | `Items.openedChests` | the host | ChestOpenedRpc | no |
| Riding (A-104) | `NetPlayerLook.Mount` (ghost id) | the rider | ghost field | no |
| Ownership, theft | none | | | |

**Three problems carry through the rest of this note.**
- **Ids:** byte ids run out soon. Fish species, ores, plant yields and folk wear are all coming, and the empty value
  is already split: 255 in the inventory, 0 (id + 1) in kits.
- **Authority:** a player's own machine decides its inventory and gear. That is fine for looks, but it means items
  can be duplicated, and the host can't check theft or quests.
- **Saving:** nothing about the world is saved, so everything taken, dropped, stolen or killed resets on restart.

## 2. The item definition

**An asset per item.** Each item is an `ItemAsset` ScriptableObject in `Assets/Game/Data/Items/`, which follows the
"content is data" rule. The C# array goes. The asset holds:
- **Key:** a stable string such as `sword` or `glasses`, never changed or reused after it ships.
- **Name:** the display name, from the language archive once one exists.
- **Stack, Weight, Value** (in coin, for traders and refunds).
- **Fits:** the slots it can go in, as a flags field: hand, two-handed, head, face, neck, body, back, legs, feet,
  sheath.
- **Looks:** hand look, worn look, sheathed side (hip or back), and world mesh. These are the
  `CreatureWeapon` / `CreatureArmour` / ItemMeshes values used today.
- **Stats:** Damage, Speed, Reach, Armour, Cover, Light, extra Slots, and Vision for eyewear (glasses = normal,
  goggles = bat).
- **Tags:** Food, Tool, Weapon, Wear, Fish, Junk, Key, Letter, Ore and so on, for quests, traders and brains.
- **Fixed:** a prop that can be drawn or placed but never taken (tables, the well rope, a chopping block). Zach 11-12:
  "Props, like tables, shouldn't be able to be taken."

**Numeric id.** The id is a `ushort`, from one append-only registry asset, `ItemIds`.
- The registry is a list of keys, and a key's index is its id.
- Id 0 means "nothing" everywhere, which retires both 255 and id + 1.
- A removed item leaves its key in place, marked retired, so no id ever moves.
- The registry starts as today's array order shifted by one: today's id `n` becomes `n + 1`. That is a one-time move,
  done in the same row as the RPC change.
- Ids are used in memory and on the wire. Saves use keys (section 6), so the registry can change between versions
  without breaking old saves.

**Runtime.** `ItemCatalog` loads the registry and assets once at start.
- **Managed array:** for UI and Items.
- **ItemBlob:** a BlobAsset for Burst code (FolkKits.Fill, brains, FishCatch): Fits, Tags, looks, stats and Fixed
  for each id.
- **Catalog hash:** a hash of the keys in order. The world check sends it, and a client whose catalog differs is
  refused with a clear message, instead of failing to read an RPC.

**What one held item is (an ItemStack).**

```
struct ItemStack { ushort Item; ushort Count; uint Data; int Owner; }   // 12 bytes
```

- **Data:** a per-kind value, 0 for plain items, read through the item's tags. For example:
  - a fish: species row in FishCatch (16 bits) plus size in cm (8 bits);
  - a letter: who it's for (an Individual);
  - wear later on.
- **Owner:** see section 4.
- **Stacking:** two stacks merge only when Item, Data and Owner all match.
- **Fish:** a catch becomes one `Fish` item with its species and size in Data. This answers the "Old boot came in as
  Fish" stand-in from P-33 row 3: junk is its own item (`boot`, `driftwood`) or thrown back.

**Over the network:**
- **Slots and world items:** an ItemStack, 12 bytes.
- **Visible slots of a body** (section 3): the `ushort` id only.

## 3. One kit for players and folk

Zach 11-12: "the inventory and equipment systems are identical for players and NPCs". Players and folk share one
layout of places:

| Place | Holds | Seen by others |
|---|---|---|
| RightHand, LeftHand | anything Fits hand (Zach: "no off hand"); a two-handed item takes both | yes |
| Sheath | one hand item, put away (hip or back, from its look) | yes |
| Head, Face, Neck, Body, Back, Legs, Feet | wear; Face is new and holds glasses and goggles | yes |
| Bag | players: 16 + the pack's Slots; folk: 4 | no: the owner and the host only |

**Not items:** hair and coat are the body's look, and Sheathed and Block are pose. All four stay where they are.

**Post props leave the kit.** Post0 and Post1 (kneading table, hot iron, chopping block) are not carried. They become
**post set pieces**: placed records at the work anchor (rule 22), with Fixed items drawn there. A spare on a rack or
table (take-items row 2b) is a world item on that set piece.

**Folk:** the host fills the kit, as now.
- **Brains (animal lane):** they write item ids into the kit directly, so the `HeldItem` enum can retire. Until they
  do, FolkKits.Fill keeps translating, so nothing changes for them at once.
- **Replication:** CreatureNet carries the 10 visible places as `ushort`. That is 20 bytes, against 11 today, and is
  delta-compressed so it costs only when a kit changes. The bag stays on the host.

**Players:** the host holds every player's kit, the same as a folk's. Getting this right matters more than anything
else in this note: one kit, and one authority over it.
- **Changes are requests.** Equip, unequip, move, drop and use all go host-bound as `KitMoveRpc { from, to, count }`.
  The host checks the move (it fits, the item is there, two hands are free) and applies it.
- **Visible places:** a new ghost component, `NetPlayerKit` (10 × ushort), carries them to everyone.
- **The bag:** goes to its owner only, as `KitSlotRpc { place, ItemStack }`, the way ContainerSlotRpc works.
- **Lag:** the owner's machine shows a move at once and takes the host's answer when it arrives. A refused move snaps
  back. Under P-31's lag figures, that's a 100-300 ms wait in the worst case, on an inventory screen.
- **Solo:** it is its own host, so solo runs the same code and no RPC is sent.
- **Eyewear** is the Face place. Vision comes from the worn item's Vision, else the body's own value. G becomes "put
  the Face item on or take it off", and it only works if a pair is in the bag. That is a question for Zach: should
  bat folk start with glasses?

## 4. Ownership

**One owner value, an `int`:**
- `0`: nobody (wild, found, loot, the player's own drops before saved ownership);
- `Household(h)`: a house's household, with h = the house's stable record id (rule 22);
- `Settlement(s)`: a town's common property (a guards' armoury, a well bucket, a market's stock);
- `Player(p)`: a player profile (section 6).

**Derived where it lies, carried once moved.**
- **Derived, never stored, while a thing is in its place:**
  - Items in a house's room storage, and its door items, belong to that house's household.
  - A worker's spares belong to the worker's household.
  - A folk's kit belongs to its household, or to its settlement for a lodger or a guard's issue.
  - Houses themselves belong to their household.

  This matches DESIGN-npc-ownership ("owner is derived, not stored per item").
- **Carried once moved:** when a player takes the item, its ItemStack gets that owner, so a stolen item stays known as
  stolen in the bag. It clears when the owner gives or sells it, when the player pays the fine, or when it's fenced.
  Fencing is a question for Zach.

**Theft.** A take is a theft when Owner is a household or settlement that isn't the taker's, and a member or a guard
sees it. The host decides, using the existing senses code (animal lane).
- **What is recorded:** a per-player crime record in the world save (section 6). It holds who was robbed, what was
  taken and who saw it, plus a "known as a thief" level per settlement.
- **What follows:** the guard reaction and the folk mood or trust drop are animal-lane rows that read this record.
- **The worker's restock walk** (take-items row 3, and the report to a guard by mood) is the first reader. A spare
  taken from a household is the case it watches for.

**Houses and players.**
- `Player(p)` lets a player own a house, a chest or a mount later, with nothing new.
- A bought house is a stored ownership change: house record id → owner, in the world save.
- Riding (A-104) needs no ownership until mounts can be kept. Then a kept creature needs a stable id, which wild
  animals don't have. That waits for a card.

## 5. Ids for things in the world (rule 22)

Today's uid ranges are worked out from bake order:
- door*4+k;
- chests 1M+;
- rooms 100k + room*4+k;
- drops 2M+;
- spares 3M+.

They break when the world file reorders records. Instead, every seeded place has its id **from its placement record**:
- **Seeded items:** a door's starter items are (door record id, k), and a chest's or room store's items are
  (container record id, k).
- **Containers:** a container is its record id.
- **Spares:** a post's spares are (work anchor record id, k).
- **Moved items:** a dropped or moved item gets a host-issued id from a counter that is saved with the world.

**Diffs only, so the world isn't bloated.** What a seed or record gives is never stored. The save holds only:
- seeded ids that are gone;
- containers that changed, with all their slots;
- loose items: id, stack, position, cell. These are capped per cell rather than per world (256 today), oldest first.
  Whether loose items also rot or wash away is a question for Zach.

**Until the world file exists:** the record id stands in as a hash of the record's kind and baked position. That
gives the same answer on every machine, and changes only when the thing itself moves.

## 6. The saved world

There are two kinds of file, both JSON (JsonUtility, versioned, new fields allowed, as QuestSave does). Both are
written atomically: a temp file is renamed over the old one, on autosave every few minutes and on quit.

**The world save, kept by the host.** There is one per world (seed + island version), on the host's machine.
- **Header:** format version, seed, island version, catalog hash, day count and time of day. Weather stays
  seed-driven.
- **Items:** gone seeded ids, changed containers, loose items, the next item id, and opened dungeon chests by
  DungeonInfo.Id and socket.
- **Households:** dead members (house id, member, day), so a bed stays empty after a restart (A-105 row 2). Also
  restock state (a missing spare and the day it comes back).
- **Ownership changes:** house or chest to a new owner.
- **Players:** one entry per player profile who has played there:
  - kit and bag, as ItemStacks with item **keys**;
  - where they were;
  - quests: moved from quests-save.json, keeping its "lost to time" handling;
  - stats (a question: per world or per character);
  - crime record.

**The player's own file, kept on each machine:**
- **Profile id:** a random 128-bit id made once per install. It isn't an account, has no personal data, and is only
  sent to the host.
- **Look:** body, coat, hair and Individual, as today, local and cosmetic.

**Not saved, rebuilt every session:**
- the terrain and anything from the seed or records;
- animals and folk positions. Folk come back from households, minus the dead;
- weather, door and lamp state. Doors start closed and lamps follow the clock.

**Roles:**
- **Solo:** the game is the host of its own world save.
- **Hosting:** the host reads its world save at start. A joining client sends `JoinProfileRpc { profile id }`, and the
  host loads that player's entry, or starts a fresh kit for a new player. Then it sends:
  - the bag (KitSlotRpc) and visible kit (NetPlayerKit);
  - the world diffs, through the late-joiner catch-up path that exists today;
  - dead members (`KinLostRpc`), so a joined client's family talk agrees.
- **Joined client:** saves nothing about the world. Its items live in the host's world, so they can't be carried into
  another world and duplicated. That's a question for Zach: should a character carry its kit between worlds? If it
  should, the host would accept a client's kit on join, and could only cap it.
- **When the world changes under a save:** a new island version or edited records. A diff whose record id no longer
  exists is dropped and logged. A player's item whose key is retired becomes coin by its Value, the same as QuestSave's
  "lost to time" refund.

## 7. Stand-ins today, and how each moves over

| # | Stand-in | Moves to | Lane |
|---|---|---|---|
| 1 | `ItemTable.Items` C# array, byte ids | ItemAsset + ItemIds registry (same order, +1), ItemCatalog, ItemBlob | player |
| 2 | Empty = 255 (inventory) or id+1 (FolkKit) | id 0 = nothing everywhere | player |
| 3 | Byte item ids in Put/Take/ContainerSlot/Drop/ItemSpawn/Grant/Gear/Armour/Slot RPCs and their State twins | new ushort / ItemStack structs (never grow an RPC; P-28 gotcha); old structs deleted in the same row | player |
| 4 | Player inventory on its own machine, unsaved | host-held kit + bag, KitSlotRpc, world save | player |
| 5 | Player gear decided by its own machine (GearRpc, ArmourRpc, SlotRpc places) | KitMoveRpc requests, `NetPlayerKit` ghost | player |
| 6 | Eyewear: G key and `NetPlayerLook.Eyewear` (A-103 row 5) | Glasses and goggles items in the Face place; Vision from the item; the ghost field retired in the same layout change | player, then the animal lane checks vision |
| 7 | FolkKit bytes (id+1) | ushort places, the same layout as the player kit | player (CreatureData / snapshot lines, as in unify row 2) |
| 8 | FolkKit Post0/Post1 props | post set-piece records at work anchors (rule 22), with Fixed items | world lane (records), player lane (drawing) |
| 9 | `HeldItem` enum in brains | brains write item ids; FolkKits.Fill translates until then | animal |
| 10 | Uid ranges (door*4+k, 1M, 2M, 3M, 100k) | record-id-based ids; a saved counter for moved items | player; world lane for record ids |
| 11 | Spares: a host-only worker-to-uid map | (work anchor record, k), owner = the worker's household, restock state saved | player |
| 12 | No owner tags | the Owner int, derived in place, carried in ItemStack once moved | player |
| 13 | Dead household members on the host only, per session (`Household.Lost`; clients guess with WatchKinLost) | world save + KinLostRpc to joiners | animal (Households), player (save) |
| 14 | quests-save.json per machine | the player entry in the host's world save (the same "lost to time" rules) | player |
| 15 | PlayerStats in PlayerPrefs | the player entry in the world save (or the profile: question) | player |
| 16 | Opened chests and changed containers, per session | world save diffs | player |
| 17 | Fish catch as one "Fish" item; junk not an item | Fish + Data (species, size); junk items | player, animal (FishCatch keys) |
| 18 | Fishing host tally, a debug count | stays debug, unsaved | none |
| 19 | Riding: `NetPlayerLook.Mount` | unchanged; kept mounts wait for a card | none now |
| 20 | Body, coat, hair, Individual in PlayerPrefs | unchanged (the local profile file can absorb them later) | none now |

## 8. Network table changes (for one rule 11 approval)

All of these change the network layout, so old builds can't join new ones. They land together in one row and one
merge, so the layout breaks once.

**New RPCs:**
- `KitMoveRpc { byte From, To; ushort Count }`: client → host.
- `KitSlotRpc { byte Place; ItemStack Stack }`: host → owner, for the bag and on join.
- `JoinProfileRpc { ulong A, B }`: client → host, once.
- `KinLostRpc { int House; byte Member }`: host → all, on change and on join.
- `CatalogCheckRpc { uint Hash }`: host → client, beside IslandVersionRpc. It's a new struct; WorldCheckRpc is not
  grown.
- `PickupRpc`, `OpenChestRpc`: unchanged (an int id).

**Replaced RPCs (new structs, old ones deleted):**
- `DropRpc` → `DropStackRpc { byte Place; ushort Count; float3 Pos }` (the host knows what's in the place).
- `ItemSpawnRpc` → `ItemPlacedRpc { int Id; ItemStack Stack; float3 Pos }`.
- `GrantRpc` → none: grants become KitSlotRpc.
- `PutRpc` / `TakeRpc` → `PutStackRpc { int Container; byte Place; ushort Count }` / `TakeStackRpc { int Container; byte Slot }`.
- `ContainerSlotRpc` → `ContainerStackRpc { int Container; byte Slot; ItemStack Stack }`.
- `GearRpc`, `ArmourRpc`, `SlotRpc` and their State twins: retired by `NetPlayerKit`. The Hair, HairColour, sheathed
  and block places move to `NetPlayerLook`, as described under the ghost fields below.

**Ghost fields:**
- **New component `NetPlayerKit`:** 10 × `[GhostField] ushort`, for the visible places.
- **`NetPlayerLook`:** gains `byte Hair, HairColour, Sheathed, Block`, which are pose and look, moved from the slot
  RPCs. It loses `Eyewear`, now in the kit's Face place.
- **CreatureNet's folk kit:** 11 bytes → 10 × ushort. Post0/Post1 leave the kit.

**Enums and tables sent:** the item registry order (now an asset and append-only, with its hash checked at join). The
`GearSlot` enum becomes the place list in section 3 (Face added; Hand split into Right and Left; Sheath and Bag added).

## 9. Suggested rows after Zach agrees

1. **Catalog:** ItemAsset, ItemIds registry and ItemCatalog/ItemBlob, generated once from today's array, with the same
   order shifted by one. Nothing changes in play, and a fingerprint of the item table proves it.
2. **Network change, one merge:**
   - ushort ids and ItemStack;
   - host-held player kits;
   - NetPlayerKit, the CreatureNet kit and the NetPlayerLook moves;
   - the catalog check.

   Checks: solo, host and client, plus P-31's lag profile.
3. **Eyewear as items** (with the animal lane), and junk and fish species as items.
4. **Ids from records:** uids from placement records, post set pieces (with the world lane), and owner values derived
   in place.
5. **World save and player profiles:** items, containers, households' dead, quests and stats move in. Restart tests:
   take, drop and steal, quit, reload, and join again as a client.
6. **Theft record:** the record, plus the restock and report-to-guard readers. Guard reactions are animal-lane rows.

## 10. Open questions for Zach

1. **Character between worlds:** when you join a friend's world, does your character bring its inventory with it?
   Recommended: no, your items live in each world you play in, so nothing is duplicated. The look and body still
   follow you.
2. **Fish:** one Fish item with species and size, or a separate item per species (better for "bring me 3 trout"
   quests, but 30+ more rows)? Recommended: one item with species, and quests can ask for a species.
3. **Loose items:** do dropped items stay forever, or rot or wash away after some days? Recommended: food rots and
   other things stay, capped per area.
4. **Stolen goods:** do they stay marked until returned, or can they be sold on (fenced) somewhere?
5. **Glasses:** do bat folk start owning a pair? Can goggles be bought?
6. **Stats:** per world, like items, or per character, like looks?
7. **Saves:** one save per world, with a list to pick from on the host screen. Is there more than one save slot per
   world?
