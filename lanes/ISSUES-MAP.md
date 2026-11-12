# Open GitHub issues -> cards (supervisor keeps this current)

Rule: every card row that works on an issue names it in the Issue column. Agents never comment on or close issues; they name them in the report. The supervisor comments the result and report link on the issue after review. The supervisor closes an issue only after the work is merged into main and Zach has OKed it. Before writing any card, check this map, lanes/ISSUES-AUDIT.md and `gh issue list`.

Updated 2026-11-03. The supervisor re-checks `gh issue list` and BENCH-REPORTS whenever the profiler shows activity, and on every wake.

## Closed 2026-11-02 with Zach's OK
#3 #7 #9 #13 #15 #16 #17 #18 #19 #23 #29 #30 #32 #34 #41 #42 #43 #47 #48 #49 #50
Closed 2026-11-03 with Zach's OK: #11 #45 #54 #55 #56

## Resolved, waiting for Zach's OK
(none)

## Partly done -> card
| Issue | Title | Card |
|---|---|---|
| #12 | Songbirds and dormice spin while wandering | A-46 |
| #14 | Cave mouths: grey shards | W-46 row 2c |
| #46 | Animals flee a flying player: count height | A-46 |
| #8 | NPC reactions to the player's race | backlog |
| #20 | Mountain ranges dividing biome regions | backlog |
| #35 #39 #40 | Bat folk, roads, colossi (roadmap) | #40 more colossi in W-58 (Zach priority); #35 backlog; #39 cobblestones (W-34) land with M-04 |

## Open -> card
| Issue | Title | Card |
|---|---|---|
| #261 | Caves: gentle water trickling down walls from slits | W-141 (after W-134) |
| #262 | Ambient details: falling leaves, smoke vents, lava seeps | W-142 (after W-141) |
| #263 | Wispy ground fog that reacts to movement | R-14 (rendering lane) |
| #265 | Small shallow natural pools in the temperate lands | W-143 (world lane, after W-141) |
| #266 | Fishing: the player can fish | P-33 (player lane, after P-32) + A-100 (animal lane, after A-99) |
| #57 | Creature perf: staggered searches, fair flee check | A-46 row 2 |
| #85 | Camera underground | P-7 rows 1-3 |
| #99 | Crash hosting a second time | P-8 row 0 (known issue in v0.12.0 notes) |
| #58-#64 | ECS code-health review | A-45/A-47 after perf |
| #68 | Character selection tab: class -> species -> colour | A-46 (animal lane owns the player's body) |
| #69 #70 #71 #72 | Zach playtest: hermit shelters, wasteland rock collision, great mushroom, biome campgrounds | W-57 (after W-56) |
| #31 | Laptop: houses lit wrong | Needs the laptop |
| #21 #22 | Plant fidelity, underwater camera (design) | backlog |
| #24 | Dungeons: caves and ruins | Phase 1: P-7 rows 4-5 (sockets in the sunken temple rooms; pool rooms need a world tile later), A-50 spawner + behaviours; merged PR #104; host/client check bench job 21 |
| #36 | Fishing villages at river mouths | W-59, from the W-58 kit (Zach priority 2) |
| #10 #35 #37 #38 #8 | New settlements and races (bat, sea, turtle, fish folk, pygmies; race reactions) | A-66: fish folk walk in the existing FishFolk settlements; pygmies walk in the existing bat folk chambers (W-19/W-20 homes) |
| #25-#27 | Roadmap | backlog |
| #28 | Linux build | on hold |
| #84 #86 #88 | Distance shadows/fog, driftwood, lake shelf waterfall | W-77 (part), W-77b; #88 tabled by Zach 11-04 after W-92 |
| #102 | Startup hitch after island ready | W-79, A-52 |
| #81 #58 | Cave FPS, CreatureState split | A-53 (#81 CPU fine in editor; build GPU check bench job 22), A-53b (#58) |
| #87 | Flat house plots | W-80 |
| #88 | Lake shelf rework (bay, mountain side, falls follow the edge) | W-81 |
