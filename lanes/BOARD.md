# Card board

One line per open card. It is the first file the supervisor reads on resume. Lanes update their own line at every
handoff (rule 34). Status: **ready** (DONE, waits for PR or merge), **active**, **resumable** (cut off mid-row: WIP
commit pushed, next step written), **blocked** (says on what), **next** (carded, not started).
Branch tips as of 11-15 evening. Cards and details live in batches/ and the lane queues.

| Card | Issue | Lane | Status | Branch@sha | Next step |
|---|---|---|---|---|---|
| W-153 | #301 | world | ready | water/truth eaaab67f | Conflicts with main (IslandCache, IslandV2, TerrainSource): world merges main in |
| W-170 r1 | no issue | world | ready | world/log-bark b1c2ab17 | Supervisor: merge (bench 94 result first) |
| W-172 r7+ | #365 | world | active | world/playtest-1114 73b0d566; world/ashlands-lava acf0fc69 | Rows 2-6 merged #451 (fauna E764AAEC, landmarks 117 689D5348). Now: #259 rejoin crash. Then: lava re-pin, W-174 r2, row 7 |
| W-174 | #406 | world | next | world/ground-edits | Row 1 note merged #440 (approved). Next: row 2 natural cutaways |
| W-173 r1,3 | #301 #399 | world | next | world/playtest-1115 (new) | After W-174 row 1 |
| W-162 | #327 | world | next | - | Shared rock wall; aligns with W-174 seams |
| W-166 r3-4 | #336 | materials | r3 merged #452; r4 part merged #456 (temple on Stone.hlsl); sweep (items, colossi) queued after W-175 r2 | world/material-library-2 9d04c2d3 | render: point TerrainSurface at Materials/Stone.hlsl, drop forwarder
| A-126 r2-4 | #367 | fauna | merged #429 | fauna/playtest-1114 | #367 closed |
| A-108 r2-4 | #298 | fauna | merged #428, #444 | fauna/far-details | r4 merged #444 (follow-up: scramble lake-ring hash) |
| A-119 | #339 | fauna | next | fauna/ride-size | rider and mount share one size (batches/A-119.md) |
| A-113 | #313 | fauna | merged #433 | flora/tree-variety | pin 0654C913 on main; #313 closed |
| A-110 | #298 | fauna | merged #430 | fauna/one-population | - |
| A-125 r2 | #360 | fauna | merged #431 | fauna/swimmer-droplets | #360 closed; BENCH job 99 |
| M-06 r3-4 | #412-414 | fauna (lab agent gone) | r4 merged #459; #413 closed; r5 waits on W-175 r2 | fauna/lab-colour b47844c0 | material kind per segment via the looks table; Fauna_Fingerprint E764AAEC, no shader change |
| A-133 | #336 | fauna | merged #448 | | creature wood + metal via Materials/*.hlsl, forwarder deleted |
| W-175 r1-2 | #415 | materials | r1 done (audit doc); Zach 11-15 OK'd the proposal: row 2 (stalls + smithy) now, W-166 r3 merged #452 | world/material-library 14429012 (docs/agents/parts-and-units.md) | Row 2 pixel-diff shows no look change; Landmarks_V3 kept or re-pinned |
| R-25 r4-6 | #302 | render-2 | merged #439 | render/far-lights-2@92253cd3 | rows 4-6 done (row 5 partly verified: no surface lava); row 6 core cap handed off; ready for PR | Row 3 merged #423; moved from render 11-15 so render keeps R-30 r1 and its rule 11 branches |
| R-30 r1-r2c | #323 | render | main merged in (#454 conflicts: leaf transmission via PunchySurfaceLight); compile + look check pending (RAM) | render/foliage-realism@161ecc3a | when 6 GB free: Refresh, compile, backlit look check, NaN 0; draft PR waits on job 102 |
| R-22 r1 | #298 | render | resumable: far mesh WIP; Medium on shots done, Medium off + High on/off + NaN check pending (editor closed, RAM < 6 GB) | render/far-island@51e8f3e0 (WIP) | when RAM allows: list2 shot runs, sparkle NaN, seams/coast check, final commit, handoff, BENCH ask |
| W-166 r3 tail | #452 | render | resumable: code done (include swap, forwarder deleted, Pending empty); editor checks pending (RAM < 6 GB) | render/stone-include@006e6c49 | Refresh, noon pixel-diff vs main, NaN 0, EditMode MaterialLibraryTests, then ready for PR |
| R-39 | #401 #402 #302 #269 | render-2 | merged #432 | render/playtest-1115 | #401 #402 closed; #302 and #269 stay open for R-25 r4-6 and R-15. Next: R-25 r4-6 (R-30 r1 stays with render) |
| P-38 | #372 | player | merged #450 | main 27171bd2 | Rows 0-4 verified (job gear kit items, A-128 drape, eyewear head items + rule 11 wire change, Model Lab kit). Main build bug: TestHooks.Options.cs needs UNITY_EDITOR guard (A-131) |
| P-33 | #266 | player | blocked | merged #419; rows 1-3 re-checked on main ec1ea218 (11-16) | Row 4 feel tuning waits on Zach playing fishing |
| P-31 | no issue | player | merged #446 | | -netsim / -rttlog / -rejoin on main; Relay + 2nd machine untested |
| P-39 | #397 #398 | player-2 (supervisor agent) | merged #434; #397 #398 closed | player/menus@762dbbd7 (tree Punchy-docs, main ec1ea218 merged) | Rows 1-3 verified; ready to merge. Follow-up: creature Interact prompt (fauna file) joins UseTarget |
| P-42 | #25 | player-2 | rows 1-2 done, verified solo + host (client runs owed: port 7979 busy) | player/talk-replies@769a3e0d (rebased on player/menus 762dbbd7) | Talk UI shows A-131 replies (FolkTalk uses Answer.Options/Choose); after P-39 |
| P-43 | #396 | player-2 | merged #441 (joined-client pair run still owed) | - | Fold RideFitSystem into PlayerRideSystem (RideOff.Size); refresh RiderBody on body change |
| A-132 | #25 | folk | merged #438 | folk/inn-job@4b9a681a | r1+r2 done (verified), main merged, WorldTests 14/14: innkeeper works the inn porch Counter by day, wipes it and stacks mugs; a/an fix in dialogue fill |
| A-134 | #460 | folk | active: r0 audit pushed (REPORTS-animal); r1 starting; rows 2-3 wait on rule 11 OK (Items.cs, Containers.cs, Interiors.cs, QuestSave.cs, new Items/Thefts.cs + TheftRpc) | folk/ownership@8c98fc7d | r1: lodgers only use doors no household owns, more kin lines, 20-folk night-door probe |
| A-124 r2 | #358 | folk | merged #458; #358 closed | folk/gait@7aa37dc3 | Per-person gait styles (verified solo + host/client, 28 clips, pins held); BENCH crowd-cost request in REPORTS-animal |
| b/fix-audio-doors | #352 | fauna | merged #445 | | |
| b/fix-dolphin-wake | A-125 | fauna | merged #443 | | |
| b/fix-lake-ring-hash | #298 | fauna | merged #447, #449 | | lake ring and sky-bird seeds scrambled |
| fauna/lab-herds | #412 | fauna | merged #453 11-16; host+join check in next playtest build | fauna/lab-herds | stale comments: CreatureSpecies.FromHomes, FaunaDirector.Build MergeHomes |
| R-28 r2 | #312 | render-2 | merged #454 (R-23 r2), #455 (R-15 r1-3); cobble-depth held for job 86 | render/cobble-depth@ce3ff5a7 ready for PR (BC1091 fixed, job 86 unblocked); render/audit-fixes@efec19b7 ready for PR (R-15 r1-3; r4 waits on job 62); render/one-surface@5c25c3fd ready for PR (R-23 r2, job 82 pass) | Main merges; R-15 WaterPatchSystem merge approved (rule 11). Render WIP goes to render/audit-fixes-wip |
| R-23 r3 | #298 | render-2 | resumable: Sky.hlsl split WIP (no code change), editor checks pending (RAM) | render/one-surface-3@9795d6a3 | Refresh + ShaderHasError + pixel-diff vs main (expect 0), then Surface.hlsl moves per design note; compile time/variants to BENCH | Design note in render-2 handoff 11-16 |
| Job 96 | #400 | profiler | next | - | Tunnel hitch on v0.16 |
| Jobs 94, 95 | - | profiler | queued | - | Log bark; one build per person |
| P-45 | #24 | player (fishing agent) | row 0 done (herd + ride-fit PASS); now row 1 -place piece | player/dungeon-pieces | Rows 0-2 per batches/P-45.md; stop after row 3 note |
| P-44 | #255, #276 | player | parked (Zach 11-16: can wait); rescoped to walk-on-moving-bodies for colossi and the galleon | - | Row 1 design note covers both; stop for review |
| P-41 | #266 | player (supervisor agent) | next | player/take-items | Fishing merged (#419); take-items port go (supervisor 11-15) |
| M-07 r2a | #260 | lab | r2a done (editor-only window, brushes, edits file); Zach 11-15: towns and roads may be rebuilt/refit around edits; r2b bake hook waits only on W-174 | merged #425 | r2b blocked on W-174; world takes r2b after W-174 r2 (lab agent gone) |
| M-08 | #414 | lab | merged #435 | lab/set-piece-view | rows 1-3 shipped; #414 stays open for M-06 row 5 blueprints |
| M-06 r3b | #413 | lab | merged #442 (Lab species herds moved to fauna, who own Herds.cs; lab blocked on W-166 / Zach) | lab/new-species 3c1ee5f1 | Homes merge, Lab species index group (after 124 built-ins), Save as new panel, KitParts field; Frostdeer (Tundra) verified solo + host; Fauna_Fingerprint 0206544F; temple Ruin ctor dropped (public static offered) |
