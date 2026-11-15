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
| #269 | Water field and camera fixes from the shader audit | R-15 (rendering lane), job 62 |
| #270 | Perf architecture: frame budget, streaming scheduler, cluster culling | roadmap, after job 62 |
| #265 | Small shallow natural pools in the temperate lands | W-143 (world lane, after W-141) |
| #266 | Fishing: the player can fish | P-33 (player lane, after P-32) + A-100 (animal lane, after A-99) |
| #267 | Caves: climbable walls in vertical sections | W-144 (world lane, after W-143) + A-101 (animal lane, after A-100) |
| #57 | Creature perf: staggered searches, fair flee check | A-46 row 2 |
| #85 | Camera underground | P-7 rows 1-3 |
| #99 | Crash hosting a second time | P-8 row 0 (known issue in v0.12.0 notes) |
| #58-#64 | ECS code-health review | A-45/A-47 after perf |
| #68 | Character selection tab: class -> species -> colour | A-46 (animal lane owns the player's body) |
| #69 #70 #71 #72 | Zach playtest: hermit shelters, wasteland rock collision, great mushroom, biome campgrounds | W-57 (after W-56)  ; A-106 (folk lane: harsh-biome work, hermit work) |
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
| #272 | Shadow darkness jumps between tiers, far shimmer (Zach 11-12) | R-16 |
| #273 | Cave pop on entry (Zach 11-12) | W-145 |
| #274 | Crystal caves far darker (Zach 11-12) | W-146 |
| #275 | Ladders (docks, mines, roofs), cliff climbs (Zach 11-12) | W-147 |
| #276 | Roadmap: climbable colossi | A-107 (fauna lane) |
| #277 | No water discs: cave lakes, white pools, fountains (Zach 11-12) | W-148 |
| #278 | Rideable creatures, players ride players | A-104, merged #297 (closed) |
| #280 | Sea swell tiles when seen from high up (Zach 11-12) | R-17 (rendering lane) |
| #281 | Roadmap: cliff and mountain geometry (overhangs, ledges, strata) | W-149 study done; R-19 (steep rock), W-150 (cliff kit + shelves); DESIGN-cliffs.md + W-155 (11-13: scarp builder replaces the kit) |
| #282 | White bloom circles keep coming back (NaN), global guard (Zach 11-12) | R-18 (rendering lane), rule 24 |
| #284 | Waterfall sheets show flat stepped quads | R-21 (world lane) |
| #286 | Shadow flicker at medium distance; far cloud shadows unstable (Zach 11-12) | R-20 (rendering lane), after R-16 |
| #308 | Near shadows crawl on buildings and benches (Zach 11-14) | R-20 row 3 (render lane) |
| #37 #38 | Sea folk city and turtle igloos are empty; diving and breath (Zach 11-12) | A-103 (folk lane: races + residents; rows 4-5 playable sea and bat folk, #291), A-102 (fauna lane: diving, breath), P-34 (player: meter) |
| #298 | Roadmap: far views, the whole island in sight (Zach 11-13) | R-22 (world lane, after R-21), R-23 (rendering, after R-16 + R-20), A-109 (folk lane: far town life), A-110 (folk lane: far figures are the real residents), A-108 (fauna lane, after A-107) |
| #287 | Load order on jumps: big things first, short load veil (Zach 11-12) | W-151 (world lane, after W-150 1b) |
| #301 | Water meshes: kinks, dead ends, not following reality (Zach 11-13) | W-153 (world lane, after R-22) |
| #302 | Lights switch on as you approach (Zach 11-13) | R-25 (render lane, after R-20; finishes R-10) |
| #303 | Lava does not look like lava (Zach 11-13) | W-154 (world lane, after W-153 row 2) |
| #304 | Cartoon bark and cave cracks; cave vs terrain detail contrast (Zach 11-13) | R-26 (render lane, after R-23 rows 1-2) |
| #309 | render | impostor props fix, render/impostor-props |
| #311 | world W-156, render R-27, folk A-111, fauna A-112 | audit 11-13 follow-ups |
| #312 | render R-28 | cobble grout and cheap parallax |
| #313 | fauna A-113 | temperate tall trees and variety, saguaro imperfection |
| #314 | folk A-114 | pocket and produce reach |
| #315 | world W-157, folk A-115 | lantern forms and heights, lamplighter |
| #316 | folk A-116 | notice board readers and posters |
| #317 | fauna A-117 | stables, folk riders |
| #320 | render R-29 | one effects kit: erratic arcs, then steam, smoke, sparks, spray, wind |
| #321 | world W-158 | darkened seams where built faces meet |
| #322 | world W-159 | river flow as normals, not white streaks |
| #323 | render R-30 | foliage realism: leaf clusters, not toon blobs |
| #324 | world | W-160 shoreline wander |
| #325 | render R-31 | continuous grass fields; grass vs dirt fine texture |
| #326 | world | W-161 elf towns with no wood |
| #327 | world (render shared surface) | W-162 cliff and cave rock walls blended |
| #328 | render R-32 | steady sun shadows; grass shaded in mid shadow tiers |
| #329 | fauna A-118 | four-legged lying height, folded legs |
| #330 | world | W-163 wood grain on planks; static strangler roots |
| #331 | render R-33 | caked, cracked salt flat ground; shares cells with W-163 row 3 bark |
| #332 | render R-34 | wet-stone look trial (dry and wet) on boulders, cliff, cave, wall |
| #333 | render R-35 | cloud ideas from a reference shader (look and cost) |
| #334 | world | W-164 gardens level on slopes |
| #335 | world (folk follow-up) | W-165 farm fields and biome crops |
| #336 | world | W-166 material library: every kit piece states its material, one include per family |
| #337 | world | W-167 towers sized for a stair and usable rooms |
| #338 | render | R-36 no cloud shadows or sun inside caves |
| #339 | fauna | A-119 rider and mount share one size for LOD and sim |
| #340 | folk | A-120 household pets; folk pet animals; children carry pets in |
| #342 | fauna | A-121 walkers go round fires; all fires give heat |
| #345 | world | W-168 bake terrain, cliff, cave and building meshes for faster starts |
| #352 | fauna | A-122 shut doors block sound, open doors let it through (uses Doors.IsOpen, #351) |
| #354 | render | R-37 sun shadow bias test sheet (depth vs normal bias, 5 s clips) |
| #356 | fauna | A-123 folk shoulder joins, dog face, bird feathers (Zach play notes) |
| #357 | world | W-169 puddles cut off at chunk borders (hollow depth LOD0-1 only), then graded rim |
| #358 | fauna | A-124 per-person folk build and gait (one NetPlayerLook field, approved) |
| #359 | world | W-170 lying logs: rigid, bark furrows along the log; row 2 nothing wooden sways (garden fences) |
| #360 | fauna | A-125 fish and dolphins splash on exit and entry, sized by body; droplets and foam from the serpent kit |
| #361 | world | W-171 fence kit: runs round any outline, styles by culture; gardens, pens, yards on it, any size |
