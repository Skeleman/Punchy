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
| W-166 r3 | #336 | materials | stone + gear + tunnel verified (NaN 0, diffs, sheets in PunchyMilestones/W-166); main merged, tests 4/4; ready for PR | world/material-library-2 0f99276b (tree Punchy-materials) | built stone close-up into BENCH 101; render: drop the Stone.hlsl forwarder; next row 4 sweep |
| A-126 r2-4 | #367 | fauna | merged #429 | fauna/playtest-1114 | #367 closed |
| A-108 r2-4 | #298 | fauna | merged #428, #444 | fauna/far-details | r4 merged #444 (follow-up: scramble lake-ring hash) |
| A-113 | #313 | fauna | merged #433 | flora/tree-variety | pin 0654C913 on main; #313 closed |
| A-110 | #298 | fauna | merged #430 | fauna/one-population | - |
| A-125 r2 | #360 | fauna | merged #431 | fauna/swimmer-droplets | #360 closed; BENCH job 99 |
| M-06 r3 | #412-414 | lab | r3 done; r4 waits on W-166, save/re-pins on Zach | fauna/model-lab-2 f4415768 | in-memory body builder, 125 species bake identical untouched |
| A-133 | #336 | fauna | merged #448 | | creature wood + metal via Materials/*.hlsl, forwarder deleted |
| W-175 r1-2 | #415 | materials | r1 done (audit doc); Zach 11-15 OK'd the proposal: row 2 (stalls + smithy) after W-166 r3 handoff | world/material-library 14429012 (docs/agents/parts-and-units.md) | Row 2 pixel-diff shows no look change; Landmarks_V3 kept or re-pinned |
| R-25 r4-6 | #302 | render-2 | merged #439 | render/far-lights-2@92253cd3 | rows 4-6 done (row 5 partly verified: no surface lava); row 6 core cap handed off; ready for PR | Row 3 merged #423; moved from render 11-15 so render keeps R-30 r1 and its rule 11 branches |
| R-30 r1-r2b | #323 | render | Zach 11-15: option b, tone trees down (Pine/Spruce 1.4; Fir/WhitePine follow-up 1.4); retake sheet, then PR | render/foliage-realism@b80b9cf8 | r2b: FloraSpecies.LeafThinness per species (pines lift), sheets R-30/row2b. Merges clean with material-library-2. BENCH 102 queued. Next: Zach's leaf pick, then build it (shells per note) |
| R-39 | #401 #402 #302 #269 | render-2 | merged #432 | render/playtest-1115 | #401 #402 closed; #302 and #269 stay open for R-25 r4-6 and R-15. Next: R-25 r4-6 (R-30 r1 stays with render) |
| P-38 | #372 | player | merged #450 | main 27171bd2 | Rows 0-4 verified (job gear kit items, A-128 drape, eyewear head items + rule 11 wire change, Model Lab kit). Main build bug: TestHooks.Options.cs needs UNITY_EDITOR guard (A-131) |
| P-33 | #266 | player | blocked | merged #419 | Row 4 feel tuning waits on Zach playing fishing |
| P-31 | no issue | player | merged #446 | | -netsim / -rttlog / -rejoin on main; Relay + 2nd machine untested |
| P-39 | #397 #398 | player-2 (supervisor agent) | draft PR #434 | player/menus@cd837c12 (tree Punchy-docs, on take-items 487dd137) | Rows 1-3 verified; merges after P-38. Follow-up: creature Interact prompt (fauna file) joins UseTarget |
| P-42 | #25 | player-2 | rows 1-2 done, verified solo + host (client runs owed, RAM) | player/talk-replies@c34b3f4b (on player/menus) | Talk UI shows A-131 replies (FolkTalk uses Answer.Options/Choose); after P-39 |
| P-43 | #396 | player-2 | merged #441 (joined-client pair run still owed) | - | Fold RideFitSystem into PlayerRideSystem (RideOff.Size); refresh RiderBody on body change |
| A-132 | #25 | folk | merged #438 | folk/inn-job@4b9a681a | r1+r2 done (verified), main merged, WorldTests 14/14: innkeeper works the inn porch Counter by day, wipes it and stacks mugs; a/an fix in dialogue fill |
| A-124 r2 | #358 | folk | active | folk/gait (new) | Per-person gait styles; no new wire field |
| b/fix-audio-doors | #352 | fauna | merged #445 | | |
| b/fix-dolphin-wake | A-125 | fauna | merged #443 | | |
| b/fix-lake-ring-hash | #298 | fauna | merged #447, #449 | | lake ring and sky-bird seeds scrambled |
| R-28 r2, R-15 land | #312 | render-2 | active | render/cobble-depth@ce3ff5a7 ready for PR (BC1091 fixed, job 86 unblocked); render/audit-fixes@efec19b7 ready for PR (R-15 r1-3; r4 waits on job 62); one-surface next | Main merges; R-15 WaterPatchSystem merge approved (rule 11). Render WIP goes to render/audit-fixes-wip |
| Job 96 | #400 | profiler | next | - | Tunnel hitch on v0.16 |
| Jobs 94, 95 | - | profiler | queued | - | Log bark; one build per person |
| P-41 | #266 | player (supervisor agent) | next | player/take-items | Fishing merged (#419); take-items port go (supervisor 11-15) |
| M-07 r2a | #260 | lab | r2a done (editor-only window, brushes, edits file); Zach 11-15: towns and roads may be rebuilt/refit around edits; r2b bake hook waits only on W-174 | merged #425 | r2b blocked on W-174; world takes r2b after W-174 r2 (lab agent gone) |
| M-08 | #414 | lab | merged #435 | lab/set-piece-view | rows 1-3 shipped; #414 stays open for M-06 row 5 blueprints |
| M-06 r3b | #413 | lab | merged #442 (Lab species herds moved to fauna, who own Herds.cs; lab blocked on W-166 / Zach) | lab/new-species 3c1ee5f1 | Homes merge, Lab species index group (after 124 built-ins), Save as new panel, KitParts field; Frostdeer (Tundra) verified solo + host; Fauna_Fingerprint 0206544F; temple Ruin ctor dropped (public static offered) |
