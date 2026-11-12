# Player lane: state (new 2026-11-03)

Branch player-client, worktree D:/Unity/Punchy-player, based on main 27bf8c1 (first editor open imports a fresh Library).
Run by a second account on the main PC. The supervisor (main account) writes cards in `batches/P-*.md`, reviews and merges.

## Scope
Now: settings and menus, the camera, client/host sync, debug and teleport shortcuts, test tools.
Later (Zach 11-03): buildings you use: interior cells, doors and lights you interact with, picking up objects,
inventory (#26). Kit shapes and collision stay with the world lane until handed over.

## First session on this account
1. Read this file, `FILES.md`, `lanes/README.md`, `lanes/shared/unity.md`, `git.md`, `report.md`.
2. `gh auth status`; `git pull --rebase` here; check `git -C D:/Unity/Punchy-player status -sb`.
3. Work only in D:/Unity/Punchy-player. Your editor: `--project-path D:\Unity\Punchy-player`. Never send to the
   D:/Unity/Punchy or D:/Unity/Punchy-world editors; never kill their Punchy.exe or editors.
4. Build only to `Builds/Dev` inside your own tree. RAM is tight: one build at a time, quit your player runs.
5. Take the lowest open card `batches/P-*.md` not marked done in REPORTS-player.md. When a card is done, take the next open one straight away (the supervisor keeps two queued). None open: stay idle, don't poll.

## Rules
Never merge PRs, close or comment-close issues (one-line progress comments are fine). Keep line endings per file
(`git ls-files --eol` before add). Leave Assets/Temp, Assets/_Recovery, QualitySettings out of commits. Public repo:
no machine paths or personal info. Never bare `git stash`.

## In flight
- 11-11 20:20 (supervisor): **LIFTED 22:24:** ~~Bench hold until about 23:30 tonight~~** (file D:/Unity/PunchyBench/HOLD; lanes/shared/unity.md "Bench hold"). When your running test or build finishes, close your editor and players and start no Unity until the file is gone; meanwhile write code for the next item row, no editor. Your W-136-era cave-water camera row (e5f328d1) is noted; the LiveFolk_BeastResponse flake goes to the animal lane.
- 11-11 (supervisor): **P-29 row 2 answer: GO, narrow edits on player/hair.** (1) Don't touch any folk spawn or snapshot file (FaunaDirector.cs spawn, net snapshots). Hair for folk is a pure function of the already-replicated `CreatureState.Individual` and `b.GuardKit`, so work it out at draw time: add `CreatureHair.ForFolk(uint individual, GuardKit kit)` in CreatureData.cs and call it in the PoseGear Hair pass when the body is not a player's (PlayerAvatar bodies keep `Outfit.Hair`, bald included; tell a player body apart by whatever the pose data already carries, or one bool set where PlayerAvatar is known; your call, name it in the handoff). Hash only from Individual, scrambled before use (math.hash(uint) alone has no avalanche); never draw from the folk spawn RNG. (2) YES to hair's own WornCount (7 parts, not the 30-part block) in CreatureRenderSystem.cs / .Passes.cs: CompactJob total and the PoseJob offset only. The animal lane is told to keep off those lines. Done when: folk shots per culture at 3 m and 30 m (LOD by ScreenSize, rule 20), Fauna_Fingerprint still AD723954, suite green, and a BENCH-QUEUE request in the handoff (town crowd frame, main vs player/hair); no timing yourself (rule 21).
- 11-10 (supervisor): **P-28 row 0 MERGED (#258, player/integrate 6e756f7b).** Merge origin/main into player/weapons, armour, clothing (and tools, seamless, doors-open) before more rows; P-29 can start from origin/main. Earlier: P-23..P-27 DONE (thanks). NEXT = P-28 row 0: one `player/integrate` branch (main + talk
  + quests + stats, no conflicts), suite, push; the supervisor merges. Then P-28 rows 1-3 (weapons, armour, clothing), P-29
  hairstyles, P-30 seamless prototype (answer to P-25 row 3: YES, one kit type behind `-seamless`).
- 11-10 (supervisor): small row after P-28 row 0: add a read-only `Doors.IsOpen(Vector3 doorPos)` (or by door id) so A-98's
  sound occlusion (Scripts/Audio/GameAudioOcclusion.cs, merged #246) can route sound only through open doors. Today it treats
  every door as open. Your file only; the animal lane wires the call later. Windows as openings can come with P-30.
- Standing (Zach 11-10): never sit idle. With no open card, keep adding items in small verified rows (more weapon/armour/clothing
  variants, culture looks, tools like a pickaxe, fishing rod, lantern) and note each in REPORTS-player.md; the supervisor reviews.
- 11-09 (supervisor): NEXT = P-25 (door prompt, indoor camera, first person, seamless-interiors plan; Zach-approved).
  P-23 (items/gear, with stats) and P-24 (quests: tracker in the menu only, progress saved and robust to world changes)
  are APPROVED (Zach 11-10). Order after P-25: P-23, P-24, P-26, P-27 (lowest open card as usual).
- Queued after P-25 (Zach 11-09): P-26 talking to folk, then P-27 player stats. A-93 is MERGED (PR #238, main 18df736a):
  API FolkPersona.Of, FolkConversation.Topics/Ask/Respond, lines in dialogue.json `conversation`/`beastTalk`. Respond and the
  favour cooldown are host/solo-local: a joined client needs an RPC to the host (part of P-26).
- P-22 done and merged 11-09 (PR #229, main): roof undersides, small-body cave camera (#85), display modes (#78).
- P-21 merged (lights/round3). Older history: git log of this file.
- RAM: 3 other editors are open tonight (R-7, R-8, W-129). Open yours only with >= 6 GB free (start-quiet.ps1);
  if short, wait for one to close (check every 10 min, stop after 3 h).

- Supervisor 11-10: #249 merged. For P-24: `DungeonState.All(list)` / `TryGet(id, out DungeonStatus)` (Id, Type, Name, Entrance, Creatures, CreaturesLeft, Cleared, MarkedChest, MarkedChestPos). Cleared is host/solo only. Opening the marked chest is yours (Items.cs only opens temple chests). For P-23: gear goes into SpawnTables/DungeonLoot as Kind 1 (chest) rows named by ItemTable name.
- 11-10 (supervisor): R-13 (release tree, lod/screen-size) changes one value in Interiors.cs: CullNear goes from metres to DetailPolicy pixels. If you touch Interiors.cs, keep that line as R-13 leaves it; merge main after #R-13 lands.
- 11-10 (supervisor): queued P-31 (simulated lag `-netsim`, Zach asked) after P-29.
- 11-10 (supervisor): queued P-32 (menu focus follows the layout: up/down by row, Zach asked) after P-31.
- 11-10 (supervisor, Zach asked): **shots go to D:/Unity/PunchyMilestones/P-<nn>/** (sub-folders are fine, e.g. P-28/armour), never to D:/Unity/shots-* folders. At your next row, move your own D:/Unity/shots-P* and shots-tools folders there (P-28-armour -> P-28/armour, shots-tools -> P-29 or the card it was for), then delete the empty shots-* folders. Report paths in REPORTS-player.md with the new location.
- 11-10 (supervisor): future direction, not a card yet: DESIGN-npc-ownership.md (households, owned items, theft). When you build pickups or inventory, leave room for an owner tag on items.
- 11-11 (supervisor): small row when W-134 merges (world/cave-water, CaveSampling.WaterAt): ThirdPersonCamera.CaveWaterAbove should ask TerrainSource.SampleGround(float3) / CaveSampling.WaterAt instead of its own cave-water search, so the underwater switch matches swimming. Not before the merge.
