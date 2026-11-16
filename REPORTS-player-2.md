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

## P-49 r3 (#24 #27): quest-only items. DONE (verified), PR #491
Branch player/quest-items@d651e02f (off main 544d91cb, #486). Design A approved under rule 11.

**What changed**
- ItemTable gains `Quest` and `Base` fields. Three rows were appended after Glow goggles, so no existing id moves: Heirloom key (85, quest heirloom), Elder staff (86, quest elder-staff) and Lost key (87, quest lost-key). quests.json names these items.
- QuestChecks.ItemFits/CountFor: a quest-tagged item only counts for its own quest. The host's hand-in tally and the client's HasFor both use them. CheckQuestItems logs an error if one quest names another quest's item.
- ItemTable.IsRandomLoot/RandomLoot: the one random roll over the item range (Containers.cs:78) now skips tagged rows. It still uses one rng draw. I grepped Fauna, Folk and Player: every other use of Items.Length is a bounds check, a look check, a debug row, or DungeonSpawner's exact-name lookup.
- Outside-file diffs are only the lines named: Containers.cs (1), ItemMeshes.cs (1; a tagged row draws with its Base's mesh) and FolkKits.cs (1; CheckLooks skips tagged rows). Main hadn't moved at commit time, so A-134 hasn't touched Containers.cs yet. Re-check if it lands first.
- Old saves: I chose **re-place**. An active pre-change heirloom, elder-staff or lost-key quest is turned back into a take on load, which puts the new tagged item into the chest (dungeon quests) or hides it again (find quests). An old generic Key stays a plain Key and no longer completes the quest.

**Checks**
- EditMode QuestItemTests 3/3: the Heirloom key does not complete lost-key and the generic Key completes neither; the Elder staff only completes elder-staff; 20000 seeded chest fills never give a Quest row and do give every ordinary row. QuestChestTests 3/3 still pass.
- Solo cave: the Heirloom key went into chest 2051145747, was taken and handed in, reward 12 Coin. Solo ruin: the Elder staff went into marked chest 1419440206, was handed in, reward Cloak. The digest is still CAB46352.
- Host + joiner, and a late joiner (joined at 100 s) on 7983: the host placed player 2's Heirloom key and checked "carried 1 Heirloom key, 1 handed in". Both clients saw the hand-in pass.
- Old save (heirloom state 1, from before the change): "took heirloom at board 5 (from the save)", then the Heirloom key was put back into chest 2051145747.
- No exceptions in any log. No shot (the card asked for none).

**Notes for review**
- The Elder staff doesn't look like a staff when held. Gear.cs HeldFor only maps ItemTable.Staff to HeldItem.Staff. Gear.cs wasn't approved, so this is a one-line follow-up, or HeldFor could go through Base.
- I didn't verify lost-key in a run; no -dungeonquest board offered it. It's covered by the EditMode test.
- Slip: I used sed -i once to set CRLF on the new test file. The file is correct (CRLF; .meta LF).
- The editor was closed via EditorApplication.Exit. TempleGlow.mat was untouched.

Shares: extends the one item system (ItemTable rows + a Quest/Base tag, rule 33). Any later quest item is a tagged row, and random rolls go through RandomLoot. Parallel found but not folded in: Gear.HeldFor and other per-id look switches could key off Base.

## P-37 r3 (#369): seamless interiors, design (editor-free)

Source: archived player/seamless (P-30 prototype 0c5e6b89, merged 2f8b77e2 onto base 393a9c20, #258), read-only. Main is now 544 commits ahead. `git merge-tree origin/main origin/player/seamless` is clean (no conflict hunks). The merge is textual only, though: main's kit and Use-target code has changed under it, so rebuild on main rather than merge.

### 1. How doors and interiors become walk-through
- The house shell is the room. For a walk-in kit type, the kit's own wall and floor boxes become the colliders, and three things are dropped: the centre cylinder, the wall circles and the trunk circle. The plinth floor stays solid, and the door step goes up to 0.25 m.
- The door is a hinged leaf that Flora.hlsl swings. While FloraDoors says the door is shut, its DoorBox is solid. When open, the doorway is clear. No teleport, no load and no off-island room.
- Inside is decided per house, with no global cells. Interiors.InWalkIn(p, out house, out depth) uses a 32 m grid lookup of walk-in houses, and the depth (0 at the sill, 1 at 1.5 m in) drives:
  - an ambient blend from sky to room;
  - the camera IndoorArm of 2.5-3.5 m (ThirdPersonCamera, player lane);
  - the indoor audio and weather mute later.
- Furnishing: each type gets a seeded layout in the house's own frame (yawed boxes). It is baked as one instanced mesh per type and built only when the house's DetailPolicy.ScreenSize passes a threshold (rule 20, no metres), with about 8 at once. At far LOD the doorway shows a dark panel and nothing inside is built.
- Light: r2 (#490) removed the body glow, so rooms are lit by a seeded lamp or hearth per layout plus the player's torch. There is no special indoor light.
- Old path: the current teleport rooms stay behind `-teleportrooms` (on for any type not yet walk-in), until all common types are converted.

### 2. What player/seamless has and what's stale
Reusable as-is (logic, re-applied by hand):
- StormHouse WalkIn flag
- DoorBoxes that are solid only while shut (FloraSystem plus FloraDoors.IsOpen(float3, within))
- InWalkIn with the 32 m cell lookup
- the depth ambient blend
- IndoorArm
- the test flags -walkin N, -atdoor, -usedoor and -shotdir

Stale against main:
- FloraGenerator.Kit.cs, the same spots, which have moved:
  - The prototype edits the `p.Solid` path, the fat cylinder and the TrunkRadius circle (main lines 184 / 220 / 273).
  - Main has since added W-166 material Family/Rubble, M-06 KitParts and W-147 ladders around them.
  - The shell edit must carry Family through, or walls lose their brick and stone.
- StructureKit.cs is now 3418 lines, over the rule-of-2500 limit. The WalkIn/Seamless flags must go in a partial (StructureKit.Parts.cs or a new StructureKit.WalkIn.cs), not the main file.
- FloraGenerator.cs (M-06 r5 blueprint buildings on W-175 units, A-117 stables): DoorBoxes must read the blueprint's door, not the old fixed kit offset. This also fixes P-30's 1.66 m door-anchor error.
- Interiors.cs:
  - A-134 #488 moved storage to per-door household containers (Interiors.Storage, registered near players). This already matches "containers per door", so the prototype's room containers are dropped.
  - P-39 r3's single facing-cone Use pick replaces the prototype's door prompt. The doorway anchor must sit on the real doorway line for the cone to pick it.
- P-37 r1 #484 (sky hold, LoadVeil): this is the teleport path's veil. Walk-in needs none, and the teleport fallback keeps it.
- P-37 r2 #490 (no body glow, starting torch): the prototype's interior relied on the glow, so the layouts need light sources.
- Known gaps the prototype never fixed:
  - Creatures path through walk-in walls, because the circles they used are gone.
  - No furniture.
  - Never run as a joined client.

### 3. Rows (each verifiable alone)
1. World shell (world lane, the supervisor routes it): a WalkIn flag for StormHouse in a StructureKit partial. The kit walls and floor collide and the circles go, keeping W-166 Family. Door anchor from the blueprint doorway. Verify: on -walkin 1 -atdoor, you walk through the open door and are blocked when it's shut. Shot inside shows brick/stone walls. Suite passes.
2. Creature blocking: walk-in houses register their wall boxes (or a door-gap ring) with the fauna avoidance query, so creatures don't walk through walls. Fauna owns the query, so this goes to fauna or the supervisor. Verify: herd test near a walk-in house, no creature inside in 5 min.
3. Player side (player lane):
   - InWalkIn lookup in an Interiors partial
   - ambient blend
   - IndoorArm
   - Use pick on the doorway line
   - `-teleportrooms` fallback
   Verify: solo, walk in and out of StormHouse. The prompt sits on the door, the camera pulls in, and the old teleport still works under the flag.
4. Furnishing and light: seeded per-type layout plus a lamp or hearth, baked instanced and built by ScreenSize. A-134 household containers sit at the layout's store spots. Verify: a shot inside at night with no torch shows lamp light. Taking an item from a chest updates that household. Bench queue gets one cost check (rule 21, the profiler times it).
5. Two more types: the commonest house kit per culture (say House and SnowHouse/Igloo, checked against the culture tables at build time). Verify: walk in and out of three types, one shot inside each.
6. Network: host plus joined client, both walk in. The door is opened by one and seen by the other. The client sees the same furniture and containers. Late joiner too. Run on 7983. This closes the P-37 row 3 card.

### 4. Network / save (rule 11 flags, not built)
- No new RPC is expected. Layouts are seeded from the house, door state is already host-owned (FloraDoors), and household containers already sync per door (A-134).
- Possible save change: if any old teleport-room container ids still sit in saves, moving those types to walk-in orphans them. Proposal: on load, map them to the house's door container, or drop them if empty. This needs your OK before row 4.
- If row 2 needs fauna to read house walls, that's a cross-lane read of world data. No sync change.
- -teleportrooms is a client-local flag. A host and a client on different settings would disagree on where the player is. Proposal: the host's setting wins and is sent once in the join handshake (one field). Flagged for rule 11. The alternative is no flag in multiplayer builds.

Needs from world (send via supervisor first, card row 3): row 1 shell changes (FloraGenerator.Kit.cs, FloraGenerator.cs DoorBoxes, FloraSystem.cs leaf box, FloraDoors.IsOpen overload, StructureKit partial).

### P-37 r3 row 1: resumable WIP (uncompiled), player/seamless-2@66a6fa0a
- Supervisor approved: the rule 11 items (old room containers moved to the door container; `-teleportrooms` decided by the host in one additive handshake field, off for old clients), and these world files for row 1 only.
- Built editor-free, behind `-seamless` (StructureKit.Seamless, a settable static in a new partial, StructureKit.WalkIn.cs):
  - StormHouse ground door leaf is hinged and drawn with its W-166 family from MaterialLibrary, at the kit's own DoorBay door part.
  - DoorBoxes are added in FloraSystem only while FloraDoors.IsOpen(centre, 2.5 m) is false.
  - FloraDoors.Version bumps when a door crosses half open, so the boxes re-gather at once instead of at the 0.5 s refresh.
  - No wall circles, centre cylinder or trunk circle. The plinth and step are solid, and the step is 0.25 m (WalkInStep).
- Next: an editor slot (a cold import, after render's 12 GB slot). Then compile, run the suite, and check on -seamless that you walk through an open door, are blocked by a shut one, and see brick/stone walls in a shot.
- Open for row 3: the shader's own door rhythm (some doors ajar by day) may show a leaf ajar while its box is shut. Interiors should drive walk-in doors explicitly.
