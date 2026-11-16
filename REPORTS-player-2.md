# REPORTS-player-2

## P-49 row 1: dungeon quest design (#24 #27, 2026-11-16, player-2) - for review
Branch player/dungeon-quests from main 7d0e415c, no commits yet. Files: Items/Quests.cs, QuestChecks.cs, QuestSave.cs, StreamingAssets/quests.json (all player files), plus one EditMode test.

**Objective kind.** Add a sixth type, `"dungeon"`, to quests.json. New fields: `kind` ("cave" or "ruin") and the existing `item`/`count`/`reward`. It works the way `find` does, but the item is hidden in a dungeon chest instead of near the board:
- On take, the host (or solo) puts the quest's item into the named chest. This goes through the container path that already exists (SetSlot, which broadcasts ContainerSlotRpc). If the chest is full, the item is spawned on the chest's spot instead.
- Hand-in is the fetch rule: you carry `count` of `item`, and the chest has been opened (Items.ChestOpened).
- On the host, CheckHandIn also needs the chest opened, plus its existing carried tally. That stops a Key bought elsewhere from finishing the quest before anyone has been down there.
- Two sample quests: "heirloom" (cave, a Key) and "idol" (ruin).

**Naming the chest.** The quest keeps no chest id of its own. Every machine works out the chest from (board, slot):
- Take the Items.ChestSockets list, which P-47 tested as the same on every machine (sorted by id, 0 clashes).
- Keep only that `kind`, and keep the 3 chests nearest the board.
- Pick one with Scramble(board, slot, quest id).
- The result is a ChestRef {Dungeon, Type, Socket}. Its id is P-47's ChestId.

New text slots: {dungeon} (cave / ruin room) and {where} ("about 420 m north-east of {town}"), so the player gets a direction rather than a hash.

The ruin room has only 1 chest, so every ruin quest names that one. If the island has no chest of that kind, the quest is skipped when boards pick offers, like a missing item.

**What a joiner sees.** There is no new RPC and no network table change.
- Taking and handing in use TakeQuestRpc / TurnInRpc / QuestStateRpc as they are now.
- The joiner works out the same chest locally.
- "Opened" comes from the P-47 ChestOpenedRpc, which late joiners also get.
- The hidden item reaches it as a ContainerSlotRpc, and late joiners get every changed slot.
- Progress in the quest log, all computed locally: "go to the cave about 420 m NE of X" -> "take the Key from the chest" -> "hand in at the board in X".
- Each player who takes the quest gets their own copy hidden, seeded by player as find does. If one player hands in, nothing changes for the others.

**Save.** Additive only. SavedQuest gains `public int chest;` (the ChestId when it was saved). On load, if the chest the quest works out now has a different id (the world changed), the quest is "lost to time", the existing path. Old saves read chest = 0 and simply take today's chest. I won't touch folk's thefts array (A-134 r3), and I'll re-check QuestSave.cs after whichever of us lands first.

**Row 2 plan.**
- EditMode test: a pure static `PickQuestChest(refs, boardPos, board, slot, id)` gives the same ChestRef for the same input, never picks the wrong kind, and returns none for an empty list.
- Flags: `-dungeonquest cave|ruin` takes the nearest board's dungeon quest. With -openchest and -takechest from P-47, then -turnin.
- Runs on 7983: solo, host + joiner (the joiner takes, opens and hands in), and a late joiner.

**Open question:** OK to reuse an existing item (Key) as the "heirloom"? A new quest-only item would mean adding an ItemTable row, and materials' W-166 branch is in ItemDef right now.
