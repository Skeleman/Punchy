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

### P-37 r3 row 3 (player side): resumable WIP (uncompiled), player/seamless-2@2ca96053
- Four commits on top of row 1, all behind `-seamless` (off by default; `-teleportrooms` wins):
  - 3a e5407ae8: Interiors.WalkIn.cs (new partial). A walk-in house keeps its type's room index and seeds, but its door no longer teleports. Lookup by a 32 m grid plus depth past the doorway drives the indoor ambient light and the camera pull-in (IndoorArm). `-walkin N` test port.
  - 3b ee18e0bf: Doors gets a pick from inside a walk-in house (the anchor mirrored through the opening), so a shut leaf can't trap you. The Use pick opens and shuts walk-in doors through the existing door state. FloraDoors.HoldShut writes w = -1 entries for shut walk-in doors within 30 m (at most 6, free slots only).
  - 3c f65f5970: IslandVersionRpc gains `byte Seamless`. A joined client takes the host's choice before its bake, and the value resets to the launch flag on a world change.
  - 3d 2ca96053: room stores become the door's chest just inside the doorway. Store k = 0 moves there and k >= 1 stacks merge into it, with leftovers kept at the same spot. Containers aren't saved and every machine seeds them alike, so this runs at registration with nothing sent. Items gains HasContainer, MergeContainer and MoveContainer.
- For render (Flora.hlsl 93-106): the shader takes max(day rhythm, entry w), so C# can't force a leaf shut. Proposed 2-line change: a matching entry with w < 0 forces open = 0. Until then a shut walk-in door may still show ajar by day, though its box is solid.
- Outside the lane: WorldSync.cs (one additive RPC field), FloraDoors.cs (HoldShut, additive), StructureKit.WalkIn.cs (LaunchSeamless). QuestSave is untouched.
- Caveat: any new RPC field changes the NetCode RPC hash, so an older build can't join a newer one either way. "Old clients default off" only holds within the same build.
- Next: the editor slot to compile rows 1 and 3 together, run the suite, then host + client on 7983 with `-seamless`: walk in and out, door from inside, chest contents equal on both machines, ambient and camera inside.
- 11-16 follow-up: supervisor approved the Flora.hlsl change. It's done in c58c890b, touching only the door-match lines: an entry with w<0 forces the leaf shut, and open is clamped to >= 0 after the loop. CRLF kept. Uncompiled. The RPC hash note goes in the PR body and the release notes (A-124 precedent). Stopped until the editor slot.

### P-37 r3 rows 1 + 3: compiled and verified, player/seamless-2@c58c890b (11-16)
- Compile: no CS errors. Flora.shader has no errors (ShaderUtil). The dev build succeeded (about 35 min, mostly Burst AOT running alongside fishing's build).
- EditMode 17/17. PlayMode 56/58, two known failures:
  - FolkConversationTests.LiveFolk_InnkeeperAtCounter: folk's fix is on folk/innkeeper-test-order.
  - WorldTests.Setup_SpawnRainAndShot: timed out at 180 s in the suite, passes 1/1 alone.
- Host + client on 7983 with `-seamless` on the host only, each running `-walkin 10 -atdoor 10 -usedoor 10`:
  - The joiner logs "the host walks into its houses; this client follows". Both build the same 11 walk-in houses (doors 10, 13, 20, 25, 27, 28, 38, 39, 42, 44, 47).
  - Both walk through door 10 into house 0, reaching depth 1.00. Indoor ambient goes 0.55 -> 0.17 warm, and the camera arm goes 0 -> 2.6. Pushing at the side wall stops at the wall box. Both walk back out ("out of the walk-in house"). No exceptions on either machine.
  - The door chests match on both: door 10 empty, door 20 Stick x1, door 42 Mushroom x1, each "1 room stores joined".
- Solo with `-seamless`: the same walk in and out.
- Default host + client (no flag): no walk-in houses, 53 doors lead inside (42 + 11 with -seamless), and the joiner is in game. Teleport rooms are unchanged.
- Not covered by the port (manual playtest): being blocked by a shut door, and using the door from inside. The port only starts once the door is open. Both are built on DoorBoxes (added while the door is shut) and Doors.TestInside.
- model.DoorBoxes keeps its name and shape (public readonly List<KitBox> on the model), ready for fauna/walkin-walls. merge-tree with origin/main (7 ahead) is clean.
- PR body / release note: IslandVersionRpc gains a byte, so the NetCode RPC hash changes. Old and new builds can't join each other (A-124 precedent).
- Editor closed with EditorApplication.Exit. The slot is free.

## P-51 row 1 (#337): design note for walk-in towers. Stopped for review.

Branch player/tower-rooms from origin/main 2354d35d. No code yet.

**Towers the game has today**
- **StructureKit.Tower** (house corner tower). TowerChance is Dwarf 0.45, Elf 0.35, Goblin/Ogre/Snow 0.1, Human 0.18. It is a solid Wall box, 0.42 x min(width, depth) on a side. Houses are 6-8.5 m wide, so the tower is about 2.0-3.6 m across. It has slit windows every Storey, a cornice, and crenels or a pyramid roof. **It has no door**, so it can stay solid under W-167's rule: only a freestanding tower with a door must fit a stair.
- **Beacon fixture** (BeaconDeck 10.4). A solid 3 m core with an outside stair up to the deck. You can already climb it. No change.
- **Spire** (sea folk). A shell spire on the lake bed with no door. It stays solid.
- **Salt tower** (SaltTowerShape). A 7.5 m basalt body with an outside spiral of steps, a tunnel and a summit. You can already climb it and it has no rooms. No change.
- **ElfTower** (FloraForm.Tower, built by FloraGenerator.Village.Tower). One per elf grove (ElfGroveLayout), Height 10.5-15.5, radius = 0.28 x height (2.9-4.3 m). It has tapered tube walls up to 0.62 x height (6.5-9.6 m), 2-3 balcony rings, a cone roof and a porch with a door. **This is the only tower with a door.** Today the door teleports you to a room (HouseInterior has a Tower case). **It is my pick for row 2.**

**Smallest footprint** (player radius 0.35 m, head 1.7 m, KitStep 0.32, so rises of 0.28 or less as on the Beacon)
- **Spiral stair round a core:** 2.5 m clear at the least. A 0.9-1 m stair, walked on a line about 0.7 m out, is about 4.4 m per turn. A 3.2 m storey takes 12 rises of 0.27 at about 0.37 m each. That leaves no room, only a landing.
- **Stair up the inside of the wall, room in the middle:** inner radius 2.2 m or more. A 0.9 m stair ring leaves a central floor about 2.6 m across.
- **Square tower with straight flights:** 4 x 4 m inside, so about 4.8-5.6 m outside with 0.4-0.8 m walls. One flight per storey runs along the next wall each time (a quarter turn per storey): 12 x 0.27 m treads give 3.2 m of run plus a 0.8 m landing. The room on each floor is about 3 x 4 m less a 1 x 3.2 m stairwell.
- **Head room:** storeys must be 3 m or more. One storey per turn or per flight then clears 1.7 m. Goblin storeys of 2.6 m would need a half-turn landing.
- **ElfTower:** the smallest inner radius (0.85 r - 0.3 at the eaves of the smallest tower) is 2.2 m. That fits a stair up the wall with a central room. **No size change is needed.**
- The house corner tower (2.0-3.6 m) is too small for any of these. Making it enterable would need bigger houses or freestanding towers, which is a placement change. That is not in this card.

**Row 2 plan: the walk-in ElfTower, only under -seamless** (StructureKit.Seamless, already synced by IslandVersionRpc)
- **Storeys:** n = floor(walls / 3.2). That is 2-3 floors including the ground; the spacing is walls / n, which is 3.2 m or more.
- **Hollow inside:** a straight cylinder of radius 0.85 r - 0.3 inside the tapered shell.
- **Inner lining:** a second, reversed tube, so the walls show from inside.
- **Floors:** a floor disc at each storey, cut by a 100-120 degree notch where the flight comes up.
- **Stair:** a helical stair up the wall made of rotated KitBoxes, about 0.27 m per rise. It makes one turn per storey (or a part-turn plus a landing).
- **Roof access:** the top floor gets an opening onto the top balcony ring.
- **Balconies:** in seamless mode the balcony rings and window rows move onto the storey lines, which gives the floor bands #337 asks for. g.Count(s.Branches) is still drawn, so the draws that follow are unchanged.
- **Collision:** the solid cylinder collider is dropped under walk-in. Wall boxes ring the shell (about 12 segments, the doorway left open), and the floors and treads become g.Boxes.
- **Door:** the porch door becomes a swinging leaf with its DoorBox, using the same helper the kit houses use (FloraDoors.HoldShut and Doors.TestInside as in r3).
- **Interiors:**
  - generalise the r3 check (`Form == KitBuilding && StructureKit.WalkIn(shape)`) to a helper that also accepts Form.Tower;
  - AddWalkIn is given the whole wall height, so ambient light and IndoorArm stay indoors upstairs;
  - the room index and seed are still made as before, so chests and stores match the teleport room.
- **Level of detail:** the interior parts (stair, floors, lining) are built only at Lod 0/1, and their boxes come from that model (rule 20, by screen size).
- **Verify:** the port flags -walkin/-atdoor/-usedoor, plus a new -climbtower N that walks the stair to the top floor and logs height and floor at each storey. I will test host + client on 7983 with -seamless, then solo, then default (no flag, unchanged), and take near shots inside and on the stair.

**Placement and world fingerprint (rule 11)**
- **Placement:** none. Plot, height, spread and ElfGroveLayout are untouched. All of the above is gated on Seamless, which is off by default.
- **Fingerprint:** default worlds and landmark hashes are unchanged.
- **Under -seamless:** only the ElfTower model and its boxes change, not where anything stands. I see no reshuffle. If row 2 shows the smallest tower is too tight and we raise ElfTower's Height.x or Spread, that would change plot size and could move grove buildings. I will ask before doing that. The layout-free rule would allow it, but it is not needed.

**Edits outside additive kit partials** (named here for approval)
- **FloraGenerator.Village.cs**, Tower() (shared Flora file; not additive): a walk-in branch that skips the solid collider and calls into a new partial, FloraGenerator.TowerRooms.cs, which holds the stair, floors, lining, wall boxes and door leaf.
- **FloraGenerator.Houses.cs**, HouseInterior: the Tower case reports the full wall height when it is a walk-in.
- **Interiors.cs / Interiors.WalkIn.cs** (player-owned): the walk-in check is generalised, and the height covers every storey.
- **StructureKit.WalkIn.cs:** a WalkInTower helper (additive).
- **No changes to:** StructureKit.Tower, Beacon, Spire, SaltTower or shaders.
- **Open question:** kit corner towers stay solid. Say if you want a freestanding kit tower family (the 4 x 4 m straight-flight version) carded later as well.
