# Card board

One line per open card. It is the first file the supervisor reads on resume. Lanes update their own line at every
handoff (rule 34). Status: **ready** (DONE, waits for PR or merge), **active**, **resumable** (cut off mid-row: WIP
commit pushed, next step written), **blocked** (says on what), **next** (carded, not started).
Branch tips as of 11-15 evening. Cards and details live in batches/ and the lane queues.

| Card | Issue | Lane | Status | Branch@sha | Next step |
|---|---|---|---|---|---|
| W-153 | #301 | world | ready | water/truth eaaab67f | Conflicts with main (IslandCache, IslandV2, TerrainSource): world merges main in |
| W-170 r1 | no issue | world | ready | world/log-bark b1c2ab17 | Supervisor: merge (bench 94 result first) |
| W-172 r5+ | #365 | world | active | world/playtest-1114 f57c15dc | Rows 2-4 done (verified). W-174 note on world/ground-edits 2e48bec2 for review. Next: row 5 (tunnel side passages, #262) |
| W-174 | #406 | world | next | world/ground-edits (new) | Row 1 audit and design note, after world-pack lands |
| W-173 r1,3 | #301 #399 | world | next | world/playtest-1115 (new) | After W-174 row 1 |
| W-162 | #327 | world | next | - | Shared rock wall; aligns with W-174 seams |
| W-166 r3 | #336 | materials | resumable: thatch/cloth/metal done (verified) | world/material-library ea9d2dab, merged #426 (tree Punchy-materials) | Next: mark bands + Materials/Brick, Plaster, Ice.hlsl, gallery per family; then W-175 r1 |
| A-126 r2-4 | #367 | fauna | merged #429 | fauna/playtest-1114 | #367 closed |
| A-108 r2 | #298 | fauna | merged #428 | fauna/far-life | Row 4 details next |
| A-113 | no issue | fauna | re-pin | flora/tree-variety 1edb7cd8 | WorldTests pin conflicts with main (9EFF3BCF): merge main, re-pin, hand back |
| A-110 | #298 | fauna | merged #430 | fauna/one-population | - |
| A-125 r2 | #360 | fauna | merged #431 | fauna/swimmer-droplets | #360 closed; BENCH job 99 |
| M-06 r3 | #412-414 | lab | r3 done; r4 waits on W-166, save/re-pins on Zach | fauna/model-lab-2 f4415768 | in-memory body builder, 125 species bake identical untouched |
| A-133 | #336 | fauna | next | - | Creature.shader includes Materials/Metal.hlsl (then delete the Shaders/Metal.hlsl forwarder) and draws wood through Materials/Wood.hlsl; no look change, pixel-diff |
| W-175 r1 | #415 | materials | next | - | After W-166 r2: parts-and-units audit (stop for Zach) |
| R-25 r4-6 | #302 | render | next | - | Row 3 merged #423; rows 4-6 after R-30 r1 (R-34 merged #427) |
| R-30 r1 | #323 | render | active | render/foliage-realism 54a8172b | WIP leaf look; continue, verify |
| R-39 | #401 #402 #302 #269 | render-2 | active | render/playtest-1115 (new, tree Punchy-release) | Row 1 true 3D distance. Row 2: Zach picked glow o3 and darker night haze (11-15) |
| P-38 | #372 | player | in progress | player/take-items 0fa149fd | row 0 merge of main done; next row 1 (A-106 job gear as kit items), then A-128 drape, eyewear items (rule 11 OK), Model Lab PoseKit |
| P-33 | #266 | player | blocked | merged #419 | Row 4 feel tuning waits on Zach playing fishing |
| P-31 | no issue | player | ready? | player/netsim b483ec06 | Merge main in, re-check -netsim |
| P-39 | #397 #398 | player-2 (supervisor agent) | active | player/menus@a22150ae (tree Punchy-docs, on take-items 487dd137) | Row 1 done (verified); next row 2 FieldMenu + MenuTabs |
| P-42 | #25 | player | next | - | Talk UI shows A-131 replies (FolkTalk uses Answer.Options/Choose); after P-39 |
| P-43 | #396 | player | next | - | Fold RideFitSystem into PlayerRideSystem (RideOff.Size); refresh RiderBody on body change |
| A-132 | #25 | folk | active | folk/inn-job@5d86fa5b | r1 code pushed (WIP, unverified): Counter anchor on inn porches (2 lines in AnchorBaking, world code) + innkeeper post; waiting for 6 GB free to run tests |
| Job 96 | #400 | profiler | next | - | Tunnel hitch on v0.16 |
| Jobs 94, 95 | - | profiler | queued | - | Log bark; one build per person |
| P-41 | #266 | player (supervisor agent) | next | player/take-items | Fishing merged (#419); take-items port go (supervisor 11-15) |
| M-07 r2a | #260 | lab | r2a done (editor-only window, brushes, edits file); r2b bake hook waits on Zach + W-174 | merged #425 | r2b blocked; lab takes M-08 meanwhile |
| M-08 r2 | #414 | lab | **resumable**: r2 code pushed as WIP, not compiled or verified (RAM under floor, editor closed). Next: once >= 12 GB free, open the lab editor, compile, eval-loop all 83 SetPieceNames x 2 variants via ModelLab.SetPieceVariant, shoot 2 variants to M-08, then r3 | lab/set-piece-view 5b5d9b8c | Model Lab shows existing building and dungeon set pieces on the turntable, read-only, editor-only; groundwork for blueprint buildings (M-06 r5) |
