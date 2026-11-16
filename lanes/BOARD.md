# Card board

One line per open card. It is the first file the supervisor reads on resume. Lanes update their own line at every
handoff (rule 34). Status: **ready** (DONE, waits for PR or merge), **active**, **resumable** (cut off mid-row: WIP
commit pushed, next step written), **blocked** (says on what), **next** (carded, not started).
Branch tips as of 11-15 evening. Cards and details live in batches/ and the lane queues.

| Card | Issue | Lane | Status | Branch@sha | Next step |
|---|---|---|---|---|---|
| W-153 | #301 | world | ready | water/truth eaaab67f | Conflicts with main (IslandCache, IslandV2, TerrainSource): world merges main in |
| W-170 r1 | no issue | world | ready | world/log-bark b1c2ab17 | Supervisor: merge (bench 94 result first) |
| W-175 | #415 | world | merged #516 (world part) | - | Cookhouse blueprint in human villages; next for world: W-174 r6 collars, then fungi-cap brightness |
| W-174 r6 | #405 | world | row 1 done (world/collars @ 9a6a6ac9), row 2 next | - | Entrance collars: row 1 temple collar in the chunk mesh at LOD0-2, rim on the grid, shared edges canonical, guard clean. Row 2: cave mouths (inner ring = cave wall 0.4 m level set), then fungi-cap brightness |
| A-135 | #405 | fauna (paused) | queued | - | Creatures respect the W-174 hole mask: CreatureMotion.cs (~306/362) and TraceSystem.cs (~300) use GroundHoles not MouthCut only; DungeonSpawner and FaunaDirector spawns reject GroundHoles.Removed ground (goblin in temple cut) |
| W-174 | #406 | world | merged rows 1-5 (#440 #481 #503 #512 #515) | - | Tail: A-135 (fauna) |
| W-173 r1,3 | #301 #399 | world | next | world/playtest-1115 (new) | After W-174 row 1 |
| W-162 | #327 | world | next | - | Shared rock wall; aligns with W-174 seams |
| W-166 r3-4 | #336 | materials | r4 sweep DONE: r4a gate 0 px, r4b salt tower stone, r4c ground items on the library (colours in vertex colours: MPB on Colossus loses the sun under Forward+); Landmarks_V3 pass | merged PR #476 | DONE; fauna sets colossus uv3 |
| A-126 r2-4 | #367 | fauna | merged #429 | fauna/playtest-1114 | #367 closed |
| A-108 r2-4 | #298 | fauna | merged #428, #444 | fauna/far-details | r4 merged #444 (follow-up: scramble lake-ring hash) |
| A-119 | #339 | fauna | DONE (PR #464) | fauna/ride-size d2d408a3 merged | probe PASS (drawn differently 0, NaN 0); main merged, fauna 4BEFEA91 unchanged; rule 11: PlayerRide.cs 2 Riding.Pair calls |
| A-113 | #313 | fauna | merged #433 | flora/tree-variety | pin 0654C913 on main; #313 closed |
| A-110 | #298 | fauna | merged #430 | fauna/one-population | - |
| A-125 r2 | #360 | fauna | merged #431 | fauna/swimmer-droplets | #360 closed; BENCH job 99 |
| M-06 r5 | #414 | fauna | DONE, merged #469; #414 closed | - | Kit gaps carded as W-175 rows 4-6 (#415) |
| #24 p2 r4-5, P-37 r3 row 2 | #24, #369 | fauna | paused (budget). r4, r5, row 2 verified + pushed; PRs left to the supervisor | fauna/dungeon-props-r4 e62982b2; fauna/dungeon-r5 74f295d0; fauna/walkin-walls dbbb352e + fauna/walkin-walls-doors 29d4328e (after seamless-2) | r1 #470, r2 #480, uv3+loot #486, r3 #497 |
| A-133 | #336 | fauna | merged #448 | | creature wood + metal via Materials/*.hlsl, forwarder deleted |
| W-175 r1-3 | #415 | materials | r3 merged (PR #465); r3b DONE: kit Crate/Basket/LanternHead parts, partial StructureKit; Landmarks_V3 pass, 7/7 | r3b merged PR #477 | r4 DONE a83b9c35; r5 DONE 422688dc (11 building parts on the kit draw + boxes, hashes same, 15/15 + Landmarks_V3); r6 DONE 393c4588 (nested units, snap joins, FloraSpecies.UnitJson; hashes same, 19/19 + Landmarks_V3); branch world/parts-units-4 ready for review |
| R-25 r4-6 | #302 | render-2 | merged #439 | render/far-lights-2@92253cd3 | rows 4-6 done (row 5 partly verified: no surface lava); row 6 core cap handed off; ready for PR | Row 3 merged #423; moved from render 11-15 so render keeps R-30 r1 and its rule 11 branches. Lava far-light re-check done 11-16 by render (walk1, 19 shots, High): rim glow steady from 1300 m in, no pop; smoke plume pops in at 850->700 m (see REPORTS-render) |
| R-30 r1-r2c | #323 | render | main merged in (#454); compiles, NaN 0, re-framed look shots OK (merged2); main A/B done (R-30/row2c/ab-main-vs-branch.jpg): same tree shapes, leaves a little darker and less flat, no glow | render/foliage-realism@161ecc3a | A/B reviewed OK by supervisor; #457 waits on bench job 102 (cost), then merge |
| R-22 r1 | #298 | render | done, ready for PR: list2 Medium/High on+off, no seams, coast smooth, NaN 0; BENCH job 107 | draft PR #482, merges when job 107 passes | lava re-check done 11-16; editor closed; render free for a new card |
| R-22 r3 | #298 | render | editor run done: compiles, bake 1.7-2.1 s on workers, NaN 0, no pop, fingerprints unchanged; effect subtle at today's sparse cover (see REPORTS) | render/far-forest@864cbb46 (off far-island) | BENCH job 112 filed; rebase after #482, PR after job 112; cover gain deferred to row 2 haze (Zach's OK). Render now on R-31 |
| R-31 | #325 | render | rows 1-2 done; row 3 see the R-31 row below | render/grass-fields@c55dbd9f | - |
| plume reach | #302 | render | verified, ready for PR (sparkle probe asked in BENCH 108) | render/plume-reach@170f46a3 | Smoke plume fade by screen size (rule 20); BENCH line at handoff |
| W-166 r3 tail | #452 | render | DONE, merged #471; verified: Refresh clean, 0 errors, sparkle stone NaN 0, EditMode MaterialLibraryTests 4/4; terrain unchanged (forwarder only included the same file; shot diffs are clouds/waves/sway) | render/stone-include@006e6c49 | ready for PR (coordinator) |
| R-39 | #401 #402 #302 #269 | render-2 | merged #432 | render/playtest-1115 | #401 #402 closed; #302 and #269 stay open for R-25 r4-6 and R-15. Next: R-25 r4-6 (R-30 r1 stays with render) |
| P-38 | #372 | player | merged #450 | main 27171bd2 | Rows 0-4 verified (job gear kit items, A-128 drape, eyewear head items + rule 11 wire change, Model Lab kit). Main build bug: TestHooks.Options.cs needs UNITY_EDITOR guard (A-131) |
| P-33 | #266 | player | blocked | merged #419; rows 1-3 re-checked on main ec1ea218 (11-16) | Row 4 feel tuning waits on Zach playing fishing |
| P-31 | no issue | player | merged #446 | | -netsim / -rttlog / -rejoin on main; Relay + 2nd machine untested |
| P-39 | #397 #398 | player-2 (supervisor agent) | merged #434; #397 #398 closed | player/menus@762dbbd7 (tree Punchy-docs, main ec1ea218 merged) | Rows 1-3 verified; ready to merge. Follow-up: creature Interact prompt (fauna file) joins UseTarget |
| P-42 | #25 | player-2 | DONE, merged #468 | - | Talk UI shows A-131 replies |
| P-43 | #396 | player-2 | DONE: fold + RiderBody refresh already on main (a980ae54, #441); re-verified on main fd90e634 11-16: solo + joined pair on -port 7983 PASS | - | Fold RideFitSystem into PlayerRideSystem (RideOff.Size); refresh RiderBody on body change |
| B-port | - | player-2 | DONE, merged #467 | - | -port N flag (default 7979) and -join ip:port; two hosts plus joiners side by side verified |
| A-132 | #25 | folk | merged #438 | folk/inn-job@4b9a681a | r1+r2 done (verified), main merged, WorldTests 14/14: innkeeper works the inn porch Counter by day, wipes it and stacks mugs; a/an fix in dialogue fill |
| A-134 | #460 | folk | r0-r3 merged #488; r4 merged #495 (owner steps out); r5 DONE (verified) 11-16: guard dispatch held until a player is near the door, same hold as r4; awaiting review | folk/guards-at-door e11d72fa | Innkeeper test order merged #501. folk/members-follow-clock: paused (budget); design approved, tree D:/Unity/Punchy-folk at main 7b7dd49d, nothing built |
| A-124 r2 | #358 | folk | merged #458; #358 closed | folk/gait@7aa37dc3 | Per-person gait styles (verified solo + host/client, 28 clips, pins held); BENCH crowd-cost request in REPORTS-animal |
| b/fix-audio-doors | #352 | fauna | merged #445 | | |
| b/fix-dolphin-wake | A-125 | fauna | merged #443 | | |
| b/fix-lake-ring-hash | #298 | fauna | merged #447, #449 | | lake ring and sky-bird seeds scrambled |
| fauna/lab-herds | #412 | fauna | merged #453 11-16; host+join check in next playtest build | fauna/lab-herds | stale comments: CreatureSpecies.FromHomes, FaunaDirector.Build MergeHomes |
| R-28 r2 | #312 | render-2 | merged #454 (R-23 r2), #455 (R-15 r1-3); cobble-depth held for job 86 | render/cobble-depth@ce3ff5a7 ready for PR (BC1091 fixed, job 86 unblocked); render/audit-fixes@efec19b7 ready for PR (R-15 r1-3; r4 waits on job 62); render/one-surface@5c25c3fd ready for PR (R-23 r2, job 82 pass) | Main merges; R-15 WaterPatchSystem merge approved (rule 11). Render WIP goes to render/audit-fixes-wip |
| R-23 r4 | #298 | render-2 | editor-free done (WIP 3fefa203); editor after world W-176 and render plume | render/one-surface-4 (new off main) | r3 merged PR #483. Row 4: Creature close-detail and bark relief fades by screen size (rule 20) |
| R-23 r4+r5 | #298 | render-2 | DONE in editor 11-16 (HeatHaze kept in metres: physical effect, pushback accepted) | render/one-surface-5@9701f8a3, draft PR #494 | Drift pair + colossus clouds on/off done 11-16 (REPORTS); waits only on BENCH job 109 |
| R-35 r2 | #333 | render-2 | row 2 done in editor 11-16 (picks A-E, E = main at fov 60 D1); shots in PunchyMilestones/R-35 for Zach | render/cloud-picks@b7a1f7cc (after #494) | Waits on Zach's picks for row 3; BENCH draft in REPORTS to file |
| R-40 | #511 | render | done: PR #513 (bilinear PCF, one shadow per point at the hand-off); 0 errors, NaN 0, noon 90 m colossus shadow now solid | render/soft-wide-shadow@251d95b1 | review + merge (coordinator); sheet PunchyMilestones/R-40/R-40-sheet.png |
| R-31 | #325 | render | row 3 done: f is the default (3/8/15/25 m hidden 44.9/74.8/89.3/71.9%), today behind -grassproto today, e dropped; NaN 0. Disc = sun-moving ground shadow (tree), not grass/push | render/grass-fields@c55dbd9f | merge waits on BENCH 114 (pinned to 14f86b6d, all three arms) |
| R-29 | #320 | render-2 | paused (budget). Row 2 call sites wired; row 3 bolt gather by screen size done; all uncompiled, needs editor slot (port 7987) | render/effects-kit@63f794f7 (Punchy-release) | resume on coordinator message |
| #400 cull | #400 | fauna | paused (budget), not started | fauna/cull-no-stall (new off main) | CreatureRenderSystem: no same-frame Complete of the cull |
| #400 mesh | #400 | world | queued after M-07 r2b, W-172 r7 | world/chunk-mesh-budget (new off main) | Per-frame chunk-mesh budget by screen size; tunnel-mouth burst |
| Job 96 | #400 | profiler | next | - | Tunnel hitch on v0.16 |
| Jobs 94, 95 | - | profiler | queued | - | Log bark; one build per person |
| P-45 | #24 | player (fishing agent) | DONE, merged #466 | player/dungeon-pieces@44630e60 | -place piece:hall/deadend/pool, walk 0 falls, sockets checked, design note. Follow-up: 2 temple sockets in walls |
| P-44 | #255, #276 | player | parked (Zach 11-16: can wait); rescoped to walk-on-moving-bodies for colossi and the galleon | - | Row 1 design note covers both; stop for review |
| P-41 | #266 | player (supervisor agent) | DONE, on main (rods 461f138c: a rod in slot 0, seeded rods on fishing-village wharves) | - | play-mode suite still owed: P-46 row 1 |
| P-46 | #24 #266 | player (fishing agent) | DONE, merged #472 (row 1: suite on main 54 pass, 1 fail Setup_SpawnRainAndShot timeout; row 2 fb9bc62c: temple sockets_in_wall 0, hash 2D2BFAEB, pin test added) | player/p46 | Row 1: play-mode suite on main (owed by P-41). Row 2: move the 2 temple sockets out of walls (chest, interactable), re-pin the socket hash, fix tests, tell the profiler |
| P-48 | #24 | player (fishing agent) | DONE, merged #474 (row 2 report: Setup_SpawnRainAndShot is a test bug, Shots.Take stalls in forced rain) | player/ruin-walk | Ruin walk: find why dungeonwalk 2:Human falls at ruin leg 23 (and times out at leg 29); fix if in player/dungeon files, else report owner + cause. Then say whether Setup_SpawnRainAndShot timeout after -weather Clear is a test bug or weather bug (report only) |
| P-47 | #24 | player-2 | DONE, merged #473 (row 2: solo, pair and late joiner pass on 7983; clash test 89 chests, 0 clashes) | player/dungeon-chests | #24 p2 r6: cave and ruin chests open like temple chests. Row 1: design the dungeon id on OpenChestRpc/ChestOpenedRpc, show the struct to the supervisor (rule 11). Row 2: build it, host lays out loot from the socket seed, joined-pair check on own -port. Read docs/design/dungeon-spawns.md sections 1-3 |
| P-50 | #24 | player (fishing agent) | DONE, merged PR #479 | main | Setup_SpawnRainAndShot: swap Shots.Take for yield return Shots.Settled(...) like the other shot tests (P-48 row 2); run the suite on main, report pass/fail counts |
| P-37 r1-2 | #369 | player (fishing agent) | row 1 merged #484; row 2 merged #490 | main 425d1215 | No loading screen (sky hold or fade); no body glow for players, a starting torch. Row 3 seamless interiors later |
| P-35 r3 | #26 | player (fishing agent) | rows 2-5 DONE verified 11-16 | catalog-check@7c74901e, item-stack@2ce4b898, kit-wire@ca6283df, item-ids@af5290e2 (ready for PR; item-ids holds rows 3-5) | Awaiting review; coordinator decides whether fishing pauses |
| P-51 r2 | #337 | player-2 | merged #514, 11-16: elf groves keep walk-in towers (default world changed, fauna DFC4F03B); -climbtower 74/75 pass solo, 74 host+client | merged #514 | Row 3 next: roof access (hatch at top of stair) and tower windows; balconies later |
| P-51 r3 | #337 | player-2 | merged #517 | - | Balconies later |
| P-52 | #518 | player-2 | merged #519; tail: stalled-joiner fall on tower 75 (active) | - | Long frames move in <=0.05 s pieces (max 8); -stallframe/-nosubstep test hooks; retry dropped; solo + host/client pass |
| P-49 | #24 #27 | player-2 | merged #485 (rows 1-2) and #491 (r3, quest-only items); Elder staff hold merged #496 | main fce56e84 | Dungeon quests: a notice-board quest that names one cave or ruin and asks the player to bring back what its chest holds. Row 1: short design in REPORTS-player-2 (objective kind on the existing quest system, how it names a dungeon chest by the P-47 {Dungeon, Type, Socket} id, how a joiner sees progress); no network table change without asking (rule 11). Row 2: build, EditMode test, solo + joiner + late-joiner on 7983. QuestSave.cs: folk adds an additive thefts array (A-134 r3); keep your save edit additive and re-check after whichever lands first |
| M-07 r2a | #260 | lab | r2a done (editor-only window, brushes, edits file); Zach 11-15: towns and roads may be rebuilt/refit around edits; r2b bake hook waits only on W-174 | merged #425 | r2b unblocked (W-174 r2 merged #481); world takes it after W-174 r3 |
| M-07 r2b | #260 | world | editor-free done, uncompiled | world/map-edits-bake@2e2f6abb | PaintedEdits bake hook, TerrainSource.Edits, cache format 5. Rule 11 OK 11-16: edits hash in WorldSync.Signature only when edits exist. Now: overlap warnings; editor after W-174 r3 |
| M-08 | #414 | lab | merged #435 | lab/set-piece-view | rows 1-3 shipped; #414 stays open for M-06 row 5 blueprints |
| M-06 r3b | #413 | lab | merged #442 (Lab species herds moved to fauna, who own Herds.cs; lab blocked on W-166 / Zach) | lab/new-species 3c1ee5f1 | Homes merge, Lab species index group (after 124 built-ins), Save as new panel, KitParts field; Frostdeer (Tundra) verified solo + host; Fauna_Fingerprint 0206544F; temple Ruin ctor dropped (public static offered) |
- 11-16 materials: PAUSED after #508 (W-175 r4-6). W-166 rows 1-4 done, no queued row. Tree D:/Unity/Punchy-materials pruned (Library kept); resume on a fresh worktree off origin/main.
- 11-16 R-35 (#333): Zach keeps the base clouds. Row 3 lands only E (screen-size fade); A-D are dropped unless BENCH shows one is cheaper. render-2 stays paused.
- 11-16 colossus cloud dim: Zach sees no darkness change between clouds on and off; keep as is, no follow-up.
