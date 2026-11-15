# Card board

One line per open card. It is the first file the supervisor reads on resume. Lanes update their own line at every
handoff (rule 34). Status: **ready** (DONE, waits for PR or merge), **active**, **resumable** (cut off mid-row: WIP
commit pushed, next step written), **blocked** (says on what), **next** (carded, not started).
Branch tips as of 11-15 evening. Cards and details live in batches/ and the lane queues.

| Card | Issue | Lane | Status | Branch@sha | Next step |
|---|---|---|---|---|---|
| W-168 r2 | #345 | world | ready | world/world-pack e9302eb9 | Supervisor: review, merge, sync |
| W-153 | #301 | world | ready | water/truth eaaab67f | Supervisor: merge after world-pack |
| R-21 | #284 | world | ready | world/waterfall-sheets 7908190c | Supervisor: merge |
| W-170 r1 | no issue | world | ready | world/log-bark b1c2ab17 | Supervisor: merge (bench 94 result first) |
| W-172 r2+ | #365 | world | resumable | world/playtest-1114 29adc54f | Uncommitted PoolAcross/NotchFall/StreamHead in Punchy-world: compile, verify, commit |
| W-174 | #406 | world | next | world/ground-edits (new) | Row 1 audit and design note, after world-pack lands |
| W-173 r1,3 | #301 #399 | world | next | world/playtest-1115 (new) | After W-174 row 1 |
| W-162 | #327 | world | next | - | Shared rock wall; aligns with W-174 seams |
| W-166 r2 | #336 | world | next | - | Wood, shared includes, test |
| A-125 r1 | #360 | fauna | ready | fauna/swimmer-splash 96f46d7e | Supervisor: merge; tell world (WaterPatchSystem additive) |
| A-126 r2-4 | #367 | fauna | active | fauna/playtest-1114 527e2f14 | Ledge hands, shimmy prompt, steer ridden animals |
| A-108 r2 | #298 | fauna | blocked | fauna/far-life d0524eaa | Rule 11 answer from supervisor |
| A-113 | no issue | fauna | ready? | flora/tree-variety f92b1fef | Merge main in, re-pin, hand off |
| A-110 | #298 | fauna | active | fauna/one-population 70d27698 | Rows to r7b |
| A-130 | #404 | fauna | next | - | Put dune palms back on W-126's clearing mask |
| A-129 r2-3 | #393 | folk | active | folk/dialogue-archive d51ab1ff | In-build dialogue.json check; row 3 design note |
| R-38 | #366 #302 #269 | render | active | render/playtest-1114 c1b30b46 | Rows done so far ready; row 2 needs far-lights 5fe0ce36 |
| R-20 r4 | no issue | render | ready | render/cloud-blocks a0311856 | Supervisor: merge |
| R-25 r3 | #302 | render | ready? | render/far-lights 5fe0ce36 | Land before R-38 row 2 |
| R-30 r1 | #323 | render | resumable | render/foliage-realism 54a8172b | WIP leaf look; continue, verify |
| R-34 r2 | #332 | render | ready | render/stone-trial 298f2084 | Base rock from the stone function, wet adds shine (Zach 11-15) |
| R-39 | #401 #402 #302 #269 | render | next | render/playtest-1115 (new) | After R-38 |
| P-38 | #372 | player | ready | player/take-items 487dd137 | Merge main in, hand off, supervisor PRs |
| P-33 | #266 | player | blocked | player/fishing ef77443b | Fishing playtest build; row 4 waits on Zach's feel notes |
| P-31 | no issue | player | ready? | player/netsim b483ec06 | Merge main in, re-check -netsim |
| P-39 | #397 #398 | player | next | - | Menus d-pad/stick, Start/Select, facing prompts |
| P-40 | #396 | player | next | - | Dismount on size change, jump off |
| Job 96 | #400 | profiler | next | - | Tunnel hitch on v0.16 |
| Jobs 94, 95 | - | profiler | queued | - | Log bark; one build per person |
| P-41 | #266 | player | next (first) | player/take-items 487dd137, player/fishing ef77443b | Land take-items then fishing on main; rods in start pack and at docks |
