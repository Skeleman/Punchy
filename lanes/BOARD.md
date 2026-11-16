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
| A-130 | #404 | fauna | ready for PR | fauna/dune-groves 7b2624eb | Done: desert biomes on the clearing mask, palm groves; fingerprint 9EFF3BCF |
| M-06 r3 | #412-414 | lab | r3 done; r4 waits on W-166, save/re-pins on Zach | fauna/model-lab-2 f4415768 | in-memory body builder, 125 species bake identical untouched |
| W-175 r1 | #415 | materials | next | - | After W-166 r2: parts-and-units audit (stop for Zach) |
| A-131 | #25 | folk | active | folk/dialogue-options - | Options data + Choose (no UI), then sample talks |
| R-38 r2 | #366 | render | blocked | render/playtest-1114 c1b30b46 | Rows 0,1,3,4 merged #409; row 2 needs far-lights 5fe0ce36 |
| R-25 r3 | #302 | render | ready? | render/far-lights 5fe0ce36 | Land before R-38 row 2 |
| R-30 r1 | #323 | render | resumable | render/foliage-realism 54a8172b | WIP leaf look; continue, verify |
| R-34 r2 | #332 | render | ready for PR | render/stone-trial 550ba9e0 | Verified; BENCH main vs branch asked; squash merge |
| R-39 | #401 #402 #302 #269 | render-2 | active | render/playtest-1115 (new, tree Punchy-release) | Row 1 true 3D distance |
| P-38 | #372 | player | blocked | player/take-items 487dd137 | main merge aborted (fauna gear vs kits); rule 11 ask a/b/c in REPORTS-player |
| P-33 | #266 | player | blocked | player/fishing 461f138c | Fishing ready for PR via P-41; row 4 waits on Zach's feel notes |
| P-31 | no issue | player | ready? | player/netsim b483ec06 | Merge main in, re-check -netsim |
| P-39 | #397 #398 | player | next | - | Menus d-pad/stick, Start/Select, facing prompts |
| P-40 | #396 | player-2 (supervisor agent) | active | player/dismount (tree Punchy-docs) | Dismount on size change, jump off |
| Job 96 | #400 | profiler | next | - | Tunnel hitch on v0.16 |
| Jobs 94, 95 | - | profiler | queued | - | Log bark; one build per person |
| P-41 | #266 | player (supervisor agent) | done | player/fishing 461f138c | Ready for PR (suite not run: RAM); take-items waits on rule 11 ask |
| M-07 r1 | #260 | lab | r1 done; waits on Zach's OK + W-174 API answer | lab/map-editor 0dea2a94 | docs/agents/map-editor.md: sparse delta before BuildPlaces, empty file keeps pins |
