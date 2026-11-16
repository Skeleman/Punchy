# Card board

One line per open card. It is the first file the supervisor reads on resume. Lanes update their own line at every
handoff (rule 34). Status: **ready** (DONE, waits for PR or merge), **active**, **resumable** (cut off mid-row: WIP
commit pushed, next step written), **blocked** (says on what), **next** (carded, not started).
Branch tips as of 11-15 evening. Cards and details live in batches/ and the lane queues.

| Card | Issue | Lane | Status | Branch@sha | Next step |
|---|---|---|---|---|---|
| W-153 | #301 | world | ready | water/truth eaaab67f | Conflicts with main (IslandCache, IslandV2, TerrainSource): world merges main in |
| W-170 r1 | no issue | world | ready | world/log-bark b1c2ab17 | Supervisor: merge (bench 94 result first) |
| W-172 r6+ | #365 | world | active | world/playtest-1114 c59f8f03 | Rows 2-5 done (verified). W-174 note on world/ground-edits 2e48bec2 for review. Next: row 6 (horse-sized cave mouth) |
| W-174 | #406 | world | next | world/ground-edits (new) | Row 1 audit and design note, after world-pack lands |
| W-173 r1,3 | #301 #399 | world | next | world/playtest-1115 (new) | After W-174 row 1 |
| W-162 | #327 | world | next | - | Shared rock wall; aligns with W-174 seams |
| W-166 r3 | #336 | materials | active: thatch/cloth/metal/plaster/brick done (verified) | world/material-library d321e4c3 (tree Punchy-materials) | Next: ice, gallery; then W-175 r1 |
| A-126 r2-4 | #367 | fauna | merged #429 | fauna/playtest-1114 | #367 closed |
| A-108 r2 | #298 | fauna | merged #428 | fauna/far-life | Row 4 details next |
| A-113 | #313 | fauna | merged #433 | flora/tree-variety | pin 0654C913 on main; #313 closed |
| A-110 | #298 | fauna | merged #430 | fauna/one-population | - |
| A-125 r2 | #360 | fauna | merged #431 | fauna/swimmer-droplets | #360 closed; BENCH job 99 |
| M-06 r3 | #412-414 | lab | r3 done; r4 waits on W-166, save/re-pins on Zach | fauna/model-lab-2 f4415768 | in-memory body builder, 125 species bake identical untouched |
| A-133 | #336 | fauna | done, ready for PR | fauna/material-library@def7b902 | creature wood + metal via Materials/*.hlsl, forwarder deleted; diff only on spear shaft; tests 2/2, sparkle 0 |
| W-175 r1 | #415 | materials | next | - | After W-166 r2: parts-and-units audit (stop for Zach) |
| R-25 r4-6 | #302 | render-2 | active | render/far-lights-2@2769270d | row 5 handed off (lava kind; no surface lava on island); next row 6 town far points | Row 3 merged #423; moved from render 11-15 so render keeps R-30 r1 and its rule 11 branches |
| R-30 r1 | #323 | render | active | render/foliage-realism 9cec43a5 | -leaftrial a/b/c built; next: run trial.txt per option (waiting on 6 GB RAM), sheet + note. Rule 11 merges pushed: cobble-depth 589ceeb6, one-surface 1a3b0f12, audit-fixes fa29fc64 (compile check pending) |
| R-39 | #401 #402 #302 #269 | render-2 | merged #432 | render/playtest-1115 | #401 #402 closed; #302 and #269 stay open for R-25 r4-6 and R-15. Next: R-25 r4-6 (R-30 r1 stays with render) |
| P-38 | #372 | player | in progress | player/take-items 0fa149fd | row 0 merge of main done; next row 1 (A-106 job gear as kit items), then A-128 drape, eyewear items (rule 11 OK), Model Lab PoseKit |
| P-33 | #266 | player | blocked | merged #419 | Row 4 feel tuning waits on Zach playing fishing |
| P-31 | no issue | player | ready? | player/netsim b483ec06 | Merge main in, re-check -netsim |
| P-39 | #397 #398 | player-2 (supervisor agent) | draft PR #434 | player/menus@cd837c12 (tree Punchy-docs, on take-items 487dd137) | Rows 1-3 verified; merges after P-38. Follow-up: creature Interact prompt (fauna file) joins UseTarget |
| P-42 | #25 | player-2 | active (branch on player/menus) | - | Talk UI shows A-131 replies (FolkTalk uses Answer.Options/Choose); after P-39 |
| P-43 | #396 | player-2 | done, ready for PR (player/ride-fit a980ae54; pair run not done, RAM) | - | Fold RideFitSystem into PlayerRideSystem (RideOff.Size); refresh RiderBody on body change |
| A-132 | #25 | folk | ready for PR | folk/inn-job@b80f3206 | r1+r2 done (verified): innkeeper works the inn porch Counter by day, wipes it and stacks mugs; a/an fix in dialogue fill |
| Job 96 | #400 | profiler | next | - | Tunnel hitch on v0.16 |
| Jobs 94, 95 | - | profiler | queued | - | Log bark; one build per person |
| P-41 | #266 | player (supervisor agent) | next | player/take-items | Fishing merged (#419); take-items port go (supervisor 11-15) |
| M-07 r2a | #260 | lab | r2a done (editor-only window, brushes, edits file); r2b bake hook waits on Zach + W-174 | merged #425 | r2b blocked; lab takes M-08 meanwhile |
| M-08 | #414 | lab | merged #435 | lab/set-piece-view | rows 1-3 shipped; #414 stays open for M-06 row 5 blueprints |
| M-06 r3b | #413 | lab | active | lab/new-species (fresh from main) | supervisor-approved rule 11: CreatureSpecies Homes field merged by FaunaDirector.Build, Lab-made species in their own group indexed after all built-ins (no renumbering), KitStructure keeps its KitPart list, SunkenTemple Ruin ctor; owners told |
