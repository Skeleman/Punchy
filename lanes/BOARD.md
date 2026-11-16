# Card board

One line per open card. It is the first file the supervisor reads on resume. Lanes update their own line at every
handoff (rule 34). Status: **ready** (DONE, waits for PR or merge), **active**, **resumable** (cut off mid-row: WIP
commit pushed, next step written), **blocked** (says on what), **next** (carded, not started).
Branch tips as of 11-15 evening. Cards and details live in batches/ and the lane queues.

| Card | Issue | Lane | Status | Branch@sha | Next step |
|---|---|---|---|---|---|
| W-153 | #301 | world | ready | water/truth eaaab67f | Conflicts with main (IslandCache, IslandV2, TerrainSource): world merges main in |
| W-170 r1 | no issue | world | ready | world/log-bark b1c2ab17 | Supervisor: merge (bench 94 result first) |
| W-172 r3+ | #365 | world | active | world/playtest-1114 545782ea | Row 2 done (verified). Row 3: built pieces (stair top 2970,-879; ladders 3020,-901; ruin holes 3547,350; camp buildings) |
| W-174 | #406 | world | next | world/ground-edits (new) | Row 1 audit and design note, after world-pack lands |
| W-173 r1,3 | #301 #399 | world | next | world/playtest-1115 (new) | After W-174 row 1 |
| W-162 | #327 | world | next | - | Shared rock wall; aligns with W-174 seams |
| W-166 r2 | #336 | materials | active | world/material-library (tree Punchy-materials) | Wood library, shared includes, test |
| A-126 r2-4 | #367 | fauna | active | fauna/playtest-1114 527e2f14 | Ledge hands, shimmy prompt, steer ridden animals |
| A-108 r2 | #298 | fauna | ready for PR | fauna/far-life 1a3a7131 | Main merged in; island cache Format 4 = one rebake; fingerprint 859112C9 |
| A-113 | no issue | fauna | ready? | flora/tree-variety f92b1fef | Merge main in, re-pin, hand off |
| A-110 | #298 | fauna | active | fauna/one-population 70d27698 | Rows to r7b |
| M-06 r3 | #412-414 | lab | r3 done; r4 waits on W-166, save/re-pins on Zach | fauna/model-lab-2 f4415768 | in-memory body builder, 125 species bake identical untouched |
| W-175 r1 | #415 | materials | next | - | After W-166 r2: parts-and-units audit (stop for Zach) |
| R-38 r2 | #366 | render | ready for PR | render/far-lights 4b95c112 | Verified by the R-25 r3 walk-in strip; merges with far-lights |
| R-25 r3 | #302 | render | ready for PR | render/far-lights 4b95c112 | Verified no pop on approach; BENCH job 76 before merge |
| R-30 r1 | #323 | render | active | render/foliage-realism 54a8172b | WIP leaf look; continue, verify |
| R-34 r2 | #332 | render | ready for PR | render/stone-trial 550ba9e0 | Verified; BENCH main vs branch asked; squash merge |
| R-39 | #401 #402 #302 #269 | render-2 | active | render/playtest-1115 (new, tree Punchy-release) | Row 1 true 3D distance |
| P-38 | #372 | player | blocked | player/take-items 487dd137 | option (a) sized at about 15-19 h (over a day), stopped; eyewear needs rule 11 (net look); go/no-go in REPORTS-player |
| P-33 | #266 | player | blocked | merged #419 | Row 4 feel tuning waits on Zach playing fishing |
| P-31 | no issue | player | ready? | player/netsim b483ec06 | Merge main in, re-check -netsim |
| P-39 | #397 #398 | player-2 (supervisor agent) | active | player/menus (tree Punchy-docs, on take-items 487dd137) | Row 1 d-pad menus; design note in REPORTS-player |
| P-42 | #25 | player | next | - | Talk UI shows A-131 replies (FolkTalk uses Answer.Options/Choose); after P-39 |
| P-43 | #396 | player | next | - | Fold RideFitSystem into PlayerRideSystem (RideOff.Size); refresh RiderBody on body change |
| A-132 | #25 | folk | active | folk/inn-job | Inn job so the innkeeper exchange is reachable; details row |
| Job 96 | #400 | profiler | next | - | Tunnel hitch on v0.16 |
| Jobs 94, 95 | - | profiler | queued | - | Log bark; one build per person |
| P-41 | #266 | player (supervisor agent) | next | player/take-items | Fishing merged (#419); take-items port go (supervisor 11-15) |
| M-07 r1 | #260 | lab | r1 done; waits on Zach's OK + W-174 API answer | lab/map-editor 0dea2a94 | docs/agents/map-editor.md: sparse delta before BuildPlaces, empty file keeps pins |
