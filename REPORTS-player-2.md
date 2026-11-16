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

## P-49 row 2 - dungeon quests built, verified on 7983 (ready for PR)

Branch player/dungeon-quests @ fff8df1b, merges clean onto main ead097ba.

**What's in it**
- New partial: QuestDungeons.cs (Items).
- Small hooks in Quests.cs, QuestChecks.cs and QuestSave.cs.
- Two quests added to quests.json: heirloom (cave, Key, 12 Coin) and elder-staff (ruin room, Staff, Cloak).
- The chest comes from the pure Items.PickQuestChest: the 3 nearest chests of the quest's kind, then a scrambled pick by (board, slot, quest id).
- No new RPC, and no network table change.
- When a quest is taken, the host puts that player's copy of the item into the chest through HostPut, or on the ground beside the chest if it's full.
- Hand-in needs the chest opened, on the client and in the host's CheckHandIn.
- Save change is additive: `int chest`. If the quest now names a different chest, it's "lost to time" (the cave or ruin room it named is gone).
- Saves and the test flag wait until the chest list has stopped changing, because caves stream in over a few frames.
- Test flag: `-dungeonquest cave|ruin`. It opens the quest's own chest directly, because P-47's -openchest picks FirstChest, not the quest chest.

**Checks**
- EditMode QuestChestTests: 3/3 pass. They cover the same answer every time, the right kind only, one of the 3 nearest, input order not mattering, and none when there's no chest of the kind (temple, empty, unknown kind).
- Digest `CAB46352 over 13 board slots, 89 chests` was identical on every run: solo, host, joiner and late joiner. Host and client pick the same chest for every board slot.
- Solo cave (fresh save): took heirloom, Key put into cave chest 2051145747 "about 150 m south-west of DwarfMiningCamp", opened, taken, handed in.
- Solo ruin: elder-staff, Staff into the marked ruin-room chest 1419440206 (about 1500 m SW), handed in, Cloak reward.
- Host + joiner: the joiner took heirloom and the host placed the joiner's Key. The joiner saw `Stone x2, Stone x2, Key x1`, opened the chest, took the Key and handed it in. The host checked "carried 1 Key, 1 handed in" and paid 12 Coin.
- Late joiner (joined at 140 s, after the host had done the same quest itself): the same chest and contents, and the hand-in passed.
- A save from an earlier run restored heirloom with a matching chest id.
- No exceptions. The joiner logged only the usual serverTick warnings.

**Notes for review**
- The Key is shared with lost-key: holding the heirloom Key also satisfies an active lost-key quest, and the board offers that hand-in first. A quest-only item after W-166 r4 would fix this.
- The ruin room is about 1500 m from DwarfMiningCamp because there are few ruin chests.
- Opened is shared world state (P-47). Once anyone has opened the chest, a later taker's log skips straight to "take the Key from the chest".
- A restored save puts the item back into the chest each session. Chests aren't saved, so this matches.
- Housekeeping: the editor opened behind a "Recovering Scene Backups" dialog, left by the earlier taskkill. I answered No (nothing copied into Assets). The editor was closed with EditorApplication.Exit. TempleGlow.mat was untouched.

Shares: Items partials (Quests.cs, QuestChecks.cs, QuestSave.cs: additive field; folk's thefts array is not on main yet), StreamingAssets/quests.json, new Assets/Tests/EditMode/QuestChestTests.cs. Read-only use of Punchy.Fauna.DungeonSpawner.Scramble. No fauna, network table or landmark files touched.
