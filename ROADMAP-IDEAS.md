# Roadmap ideas (Zach)

Zach's long-range ideas, paraphrased; one entry per idea. His own wording is on the issue named. The supervisor
keeps this file; lanes read it only when a card names a section. Before carding an entry, check the issue,
`lanes/ISSUES-MAP.md`, cards in flight and main.

Standing: the aim and its priorities are in COORDINATION.md. The layout is a test bed and may change
(`lanes/shared/world-data.md`). Core tenet: small, varied, seeded details everywhere, kept sparse.
One system per kind of thing (rule 33): every entry below extends an existing system and its whole family.

Status words: **Shipped** (on main), **In flight** (a card or branch is working on it), **Next** (carded or
asked, not started), **Later** (idea only). Updated 11-15 against main and merged PRs.

## World and far view
- **Far views (#298).** The whole island in sight: ground to the horizon, far forest, far life. Shipped: far
  terrain ring to about 3 km (W-131), tree and shrub impostors (R-7, R-11), far herds (A-108 row 2, PR #428), one population (A-110, PR #430). Next: R-22 (whole-island ground,
  far forest, weather haze, approved 11-13), R-23 (one lighting path). Shipped: A-108 row 4 details (far eagle, herd dust, dawn lake rings, PR #444); R-23 row 2 one surface lighting path (PR #454); row 3 Sky.hlsl split and cloud/wide shadow dim only direct sun on every lit surface (PR #483). In flight: R-23 row 4 fades by screen size; R-22 row 1 whole island (draft PR #482).
- **Cliffs (#281 closed, #327; DESIGN-cliffs.md). Top concern (Zach 11-15).** Cliffs built as the ground itself. Shipped: steep ground reads as layered
  rock (R-19), mountain tunnels (W-138). Shipped: the scarp builder W-155 (big cliffs and river banks, PR #377), the cliff kit on sea cliffs and mesas (W-150, PR #386); walk and climb (row 3) and the shared rock wall with
  caves (W-162, #327) come after; caves climb by the same method (W-144's routes fold into W-162). Shelves with homes, ruins and flora are later.
- **Rivers (#301, W-153).** No kinks, dead ends or water walls. Rows 1-2 shipped #536 (uphillGraph was a probe artifact at lake edges, fixed #537); row 3 river notches + small falls shipped #538 (small falls made standing water mesas, reverted #539); rows 4-6 on hold. 11-17: Zach wants a serious rework of river geometry, since water keeps escaping the ground; design merged #539 (docs/agents/river-rework.md), Zach chose the full rework (6 rows), In flight. 11-17: subdivide the water mesh at waterfalls: a curtain strip built from each fall profile (lip line, arc out and down, ragged edges, foot pool), rows by ScreenSize; added as rework row 3b (one course map, 11-17: rivers stuck going slightly uphill should cut a short steep notch through the rise (scarp/RockWall banks from W-162), or a small fall where the drop is big. Rows 1-3 merged #541, row 3b fall curtain #543 (11-17); rows 4-6 next. Row 4 lake clip only in river channels shipped #554 (lake walls 307 to 5; row 5 dropped); row 3 tail fall-lip rock shoulders #562, lip slab gone + soft uneven curtain edges #568 (11-17); curtain dark lines gone #575 (11-17). Row 6 sheet edges Shipped #581 (11-18): no hanging edges or gaps at 4 of 5 reaches; perched lake at -490,546 Next (water-truth row).
  profiles and lakes on the finished ground). Shorelines that wander (W-160, #324): row 1 Shipped #597 (11-18); row 2 stronger and ragged foam rim In flight, render-2. River flow as surface ripples, not white streaks (W-159, #322): row 1 Shipped #590, row 2 churn foam Shipped #591 (11-18); row 3 mist edge Shipped #593 (11-18); all rows done.
- **Waterfalls (#284, R-21).** One builder for every shape of fall, a subdivided curved sheet. Shipped: deforming
  water, river falls and rapids (R-9), one builder with curved curtain sheets (R-21, PR #411).
- **Lava and toxic pools (#303, W-154).** Shipped: first lava look and per-fluid underwater (PR #213). Shipped: a volcano cone in the Ashlands (W-172 lava half-row, PR #463). Next: W-154
  coarse crust normals and the far form.
- **Lights (#302, R-25).** No light switches on as you approach. Shipped: town lights steady from 600 m, no pop on approach (R-25 row 3, PR #423). Shipped: glows and flames by on-screen size, Zach's glow option 3 with darker nights, igloo windows lit at every distance (R-39, PR #432). Shipped: lava far lights and far cores that keep their hue (R-25 rows 5-6, PR #439). Shipped: smoke plumes fade by screen size, no pop-in (PR #487). Next: re-check lava far lights once surface lava exists. Shipped: the lamplighter lights towns at dusk (A-115, PR #374). Shipped: fishing walkway lanterns light only after dark, no noon beach glare (PR #549).
- **Shadows (#286 closed; bias #354 closed 11-15: no visible difference, stays 0.1/0.5).** Shipped: one shadow darkness (R-16), steady near and high-preset sun shadows (R-20 row 1,
  R-32), blue moon shadows (R-32), no sun or cloud shadow through cave roofs (R-36), smooth cloud shadow reads (R-20 row 4, PR #410).
- **Stylized realism (#304, R-26), foliage (#323, R-30), grass fields (#325, R-31), salt-flat cracks (#331,
  R-33, shipped PR #579), wet stone (#332, R-34, shipped PR #427), clouds from a reference shader (#333, R-35).** R-26 row 1 audit done; row 2 recessed cave cracks + soft bark Shipped #583 (11-18); row 3 cave wall vs ground detail match Shipped #586 (11-18). R-35 cloud E waits on Job 111.
- **Sun moves smoothly (R-38, 11-15).** Sun position every 0.1 s as a trial. Shipped (R-38, PRs #409, #423).
- **Caves.** Shipped: screen-size cave LOD, cave water, dungeon texturing and collision. Shipped: no paddy spikes on slopes (W-172 row 1, PR #384). Shipped: playtest
  fixes and tunnel variety (W-172 rows 2-6, PR #451). Shipped: cave walls that fit the place (W-172 row 7, PR #507; brighter fungi caps shipped #521; next: volcanic cave details; moss only near light or damp,
  glowing fungi deep, carvings near ruins, fossils in sedimentary rock, frost, salt crust, flowstone, tide
  marks, soot near camps, roots under forest). Later: small-animal passages to secret rooms and river-carved
  tunnels (#370).
- **Floating islands (#256).** Later. Walkable, some with caves, one with a new kind of town; reached by flying.
- **World file (#260, #345).** Shipped: the baked island is saved and loaded instead of rebuilt (W-140), start
  ledger (W-168 row 1), cave sockets and the island file in every build (W-168 row 2, PR #407). Next: bake meshes into the shipped file (#345). Hand-editing is far off; rule 22.
- **Desert groves (#404).** Shipped: palms and palo verde in groves with clearings (A-130, PR #421).
- **Plant fidelity (#21)** experiments. **Cluster culling (#270)** waits for profiler numbers.

## Settlements
- **Sea folk city and turtle folk villages (#37).** Shipped: built and populated (A-103). Far town figures fade by
  screen size (A-109, PR #373).
- **Fishing and marsh villages, stables, ladders.** Shipped (W-119, W-122, A-117, W-147).
- **One material library (#336, W-166). Top concern (Zach 11-15):** the same look for a material whether built, held,
  dropped, on an animal or on a colossus. In flight: row 1 audit done 11-14 and confirmed 11-15; row 2 (wood, shared
  includes, test) next for the world lane, then the other families and the creature, item and colossus calls. Shipped: one wood library for every built piece; thatch, cloth and metal in shared files (rows 2-3a, PR #426); plaster, brick and ice includes (PR #437). Shipped: creature gear wood and metal on the library (A-133, PR #448). Shipped: built stone (cut and rubble), cave linings and creature cloth and bone on the library (row 3, PR #452); sunken temple ashlar (row 4 part, PR #456); terrain reads the library's stone directly (PR #471). Shipped: items and the salt tower on the library, colossus shader reads a family channel (row 4, PR #476). Shipped: each colossus names its families, oak wood, golem stone, lizard bone, mammoth ice (PR #486). W-175 row 2 shipped (PR #462).
- **Farm fields (#335, W-165), elf towns without wood (#326, W-161), wood grain and strangler roots (#330, W-163).** W-163 row 1 wood grain Shipped #573 (11-17); row 2 roots Shipped #576 (11-17); row 3 cracked bark Shipped #578 (11-17); W-165 row 1 fields Shipped #580 (11-17), row 2 cheaper crops + desert/elf crops Shipped #582 (11-18); rice tufts in still shallows Shipped #584 (11-18); sea-village plots Shipped #589 (11-18); W-161 elf towns without wood Shipped #585 (11-18), hazard shelters Shipped #592 (11-18).
- **Towers with real storeys (#337).** In flight as P-51 (player-2): walk-in elf towers with a climbable inner stair (#514) and roof hatch, deck and kit windows (#517) shipped 2026-11-16; later: balconies. Later: a freestanding kit tower family with straight flights (4x4 m inside).
- **Castle walls (#254).** W-182 row 1 kit units in the Lab Shipped #594 (11-18); row 2 collision and LOD trim Shipped #595; castle quoins and putlog holes Shipped #596; world placement Later. Modular walls, towers, a gatehouse with a working, synced portcullis.
- **Port town (#255).** Later. A big pier, a boardable galleon and a busy main road.
  Zach 11-16: worth the effort, start simple; later the same day: colossi should be dynamically rideable too, so P-44 is one walk-on-moving-bodies system for colossi, the galleon and boats. Later (can wait).

## Folk and races
- **Playable races (#291).** Shipped (A-103): sea, bat and turtle folk; bat eyes see brighter.
- **Breath (A-102).** Shipped: works like stamina; air breathers resurface; sea folk never run out.
- **Households (#25, A-105).** Shipped: own homes, going home at dusk, family talk. Shipped: notice board (A-116), fire heat (A-121), doors and sound (A-122). Shipped: household
  pets (#340, A-120, PR #389). Shipped: households own houses and goods; a seen theft sends a guard, and the household refuses favours for 3 days (A-134, PR #488). Shipped: the owner steps out to confront you (A-134 r4, PR #495). Shipped: a guard called while you are inside waits, then meets you at the door (A-134 r5, PR #498). Later: paying back and trust (P-35).
- **Conversation (#25).** Shipped: personalities, a conversation API, reactions to an animal player (A-93),
  hermit chores and talk (A-106), the player talk system (P-28). Shipped: a hand-editable text archive reloaded
  live (A-129, PR #416). Shipped: dialogue options, folk side (A-131, PR #420). Shipped: the innkeeper works the inn counter (A-132, PR #438). Shipped: the player talk UI shows the replies (P-42, PR #468).
- **One population (#298, A-110).** Shipped PR #430. Far figures are a town's real people; nobody swaps as you approach.
- **Skills and attributes.** Shipped first stats (P-28). Grows as features need it.

## Creatures
- **Riding (#278, A-104).** Shipped: any creature rides one at least 50% heavier, players ride players, folk ride
  horses out of town stables (A-117) Shipped: dismount when you no longer fit the mount (P-40, PR #422). Shipped: rides keep up when either body changes mid-ride (P-43, PR #441).
- **Climbable colossi (#276, A-107).** Shipped: graded climbable sections, carried as the colossus walks. Later: walk freely on a moving colossus, shared with the galleon deck (P-44, Zach 11-16).
- **Sand and snow worms.** Shipped: breaching snow worm and sand worm colossi (A-84, Z-28).
- **Climbing (A-101).** Shipped: one climbing model, ladders, marked walls, ledge hang. Shipped: brisk ladders
  (A-126 row 1, PR #383). Shipped: A-126 rows 2-4 (ledge hands, shimmy prompt, steering ridden animals; PR #429). Next: cave routes
  climb by the cliff method (W-162).
- **Fishing (A-100, P-33).** Shipped: catch tables, fish notice the bobber, player fishing rows 1-3 (P-33, PR #419). Next: feel tuning after Zach plays it.
- **Individual builds and gaits (A-124).** Shipped: row 1 builds (PR #387) and the style draw that really varies (PR #388); row 2 per-person gait (PR #458).
- **Bodies read clearly (A-123, #356).** Shipped (PR #385): arms meet the torso, canine faces, feathered birds. Shipped: robes, aprons and capes clear the legs (A-127 PR #390, A-128 PR #394).
- **Gold variants.** Shipped: 1 in 400.

## Dungeons, items, quests
- **Dungeons (#24).** Shipped: texturing, bigger caves and ruins, a dungeon state API, collision (A-91 row 1,
  A-95). Shipped: A-91 rows 2-4 (PR #249). Shipped: set-piece placement on the shared temple rules (P-45, PR #466); temple sockets out of walls (P-46, PR #472); cave and ruin chests open like temple chests (P-47, PR #473). Dungeon walk test fixed for stacked floors (P-48, PR #474). Shipped: dungeon creatures as residents on a near-player tier, the dead stay dead (#24 p2 r2, PR #480); cave and ruin chest loot rows (PR #486). Shipped: joiners hear which residents are dead (#24 p2 r3, PR #497). Shipped: props on screen-size tiers, roost/patrol/sleep posts (r4-r5, PR #505). Next (fauna paused for budget): item sockets; new rooms wait on W-174 seams (row 4)/W-175.
- **Items and inventory (#26).** Shipped: first items and doors (P-28), pocket reach (A-114). Shipped: hand
  actions, per-item block and folk gear kits (P-38, PR #450). In flight: one item definition and saved state (P-35; item catalog shipped PR #493; item ids, stacks and the catalog sync check shipped PR #510. Next: saved state; fishing is paused).
- **Interiors (#369).** Shipped: no loading screen (P-37 r1, PR #484), cave lantern (r2, PR #490), walk-in storm houses behind -seamless (r3, PR #502), creatures kept out of them (PR #506). Next: playtest shut-door blocking and inside use, then decide whether seamless becomes the default.
- **Quests (#27).** Shipped: first quests (P-28), dungeon quests (P-49, #485), quest-only items (#491). Later: more objective types, tracking.

## Seasons
- A generic calendar: day-of-year counter, configurable year length and season count, synced. Values follow
  curves over the year and never jump at a boundary.
- Global: sun and moon arcs and day length change through the year.
- Per biome, a profile by day of year: temperature offset, weather odds, flora look (colour, leaf drop, bloom,
  snow, dormancy), fauna spawn numbers, water (desert rivers dry most of the year and fill in a monsoon; frozen
  lakes, spring floods, shrinking ponds).
- Animals and folk never read the season. Their behaviour changes only through light, temperature and storms
  (builds on A-71).
- Zach 11-16 agreed: seasons change the look and water level only (ice sheets, dry beds, snow,
  higher spring water); rivers keep their baked course. Flooding and re-routing stay Later.

## Fire
- One shared fire effect with a colour setting, used by everything that burns; Ember Forest burns one seeded
  colour per night. Shipped (#131) with 4 colours of the 7 asked for (orange, golden, silver, violet, emerald,
  crimson, black). Zach wants further tweaks later.
- Effects as reusable systems (11-14): electricity, steam, smoke, sparks, gushing water and wind each one shared effect like fire, so new places reuse them as systems grow. R-29, rule 30. Electricity shipped #540 (spires + golems, 11-17); steam and smoke shipped #565 (geysers, hot springs, mud pits, fumaroles, chimneys, 11-17); waterfall spray shipped #570 (11-17); sparks shipped #571 (11-17). Shipped (rows 1-4b, #320 closed); wind Later (ambient wind is a tile shader, not plumes).
Foliage realism (Zach 11-14): crowns as leaf clusters (cut-out cards, layering, bent normals) instead of solid toon shapes; #323, R-30.
- Shorelines wander instead of ruled lines: lapping, noise lift, ragged wet band (W-160, #324).
- Continuous grass swathes without bald spots; grass ground textured with blades, dirt speckled and grainy (R-31, #325).
- Soft mid/far shadows, with one shadow LOD per caster and no wash-out at the hand-off (R-40, #511). Shipped (#513, 2026-11-16).
- Elven cities have no wood: signs, fences and props in stone or metal (W-161, #326). Shipped #585.
- Cliffs and caves share one rock wall look, climbable grades and the ground blend at cave mouths (W-162, #327). Shipped #535; cave routes are W-181.
- Blue moon casts the shadows on blue-moon nights, so its blue light reads as coming from it (R-32 row 3, #328).
- 11-14 (Zach): procedural wood grain on planks and static strangler-fig roots on swamp trunks and cave walls (W-163, #330).
- 11-14 (Zach): caked, cracked dry ground on salt flats and cracked bark (R-33 #331, W-163 row 3).
- 11-14 (Zach): wet-stone look trial on in-game stone (R-34 #332). Shipped: wet stone is the base rock (PR #427).
- Reference shaders Zach sent (wood grain, cracked plates, wet stone, grass, clouds): D:/Unity/PunchyReferences (local, README lists licence and card).
- 11-14 (Zach): explore a reference cumulus shader for cheaper or better clouds: R-35, #333. Zach 11-16: the base clouds look best, so keep them; the A-D looks stay off unless one saves frame time. Next (render-2, when resumed): land only the screen-size fade (E, rule 20) and drop the looks.
- 11-14 (Zach): farm fields outside towns, crops for every biome, e.g. snow potatoes, agave-like ferns (W-165, #335).
- 11-16 (Zach): harvestable parts on real plants (fruit, flowers, petals, bark, parasitic growths); a picked part vanishes and the plant stays; regrows after a few in-game days, kept in day buckets, not per item (#492). Later: after P-35 ids and W-175 parts.
- 11-14 (Zach): one material library: every built piece states what it is made of, families share look rules, kinds vary colour (W-166, #336).
- 11-14 (Zach): towers sized so a stair and rooms could fit, storeys readable from outside (W-167, #337).
- 11-14 (Zach): household pets; folk pet animals by personality; children carry small animals inside (A-120, #340).
- 11-14 (Zach): a rider and mount should share one size for LOD (A-119, #339). Shipped: rider and mount step detail, think and despawn together (PR #464).
- 11-14 (Zach): no one walks through a campfire; every fire gives heat that comfort uses (A-121, #342).
- 11-14 (Zach): move more generation into a one-time bake shipped with the game so starts are faster (W-168, #345).
- 11-14 (Zach): gear, dropped items and animal parts (horn, shell, hide) share the material library (W-166, #336).
- 11-14 (Zach): one material library for everything, fur, feathers, scales and skin included, so furniture and gear can use them later (W-166, #336). Coverings rows shipped #546, Creature.shader switch shipped #548, cloth/metal rows #559, copper + felt #560, copper trim off the stone fallback #561, scarps take their biome stone (row 2) #564, stone sets gully spacing and chimneys (row 2b) #567, iron kit material for lanterns/braziers/brackets #569 (11-17).
- 11-17 (Zach): cave wall surface geometry is a function of the rock material; the per-style cave surface comes back through the library, not a cave card (W-166, #336). Shipped #542 (row 1, 11-17), wood row #544 (11-17), lib tests + Cookhouse families #545 (11-17).
- 11-17 (Zach): the same per-material rules cover every rock wall (cliff scarps, river notches): one rock-wall rule set by material. Caves first (materials row 1), scarps row 2 after the river rework frees ScarpSet/TerrainSource (W-166 #336, W-162 #327). Next. 11-18: row 2 (cave walls in the scarps' stone) done on materials/w166-scarp-stone, held until the world mouth patch lands; deep cave colour Later.
- 11-15 (Zach): every building from kit pieces so collision is predictable (#399, W-173 row 3 note first).
- 11-15 (Zach): Start for out-of-world things (settings, character, multiplayer), Select for in-world (items, quests, status, stats) (#397, P-39). Shipped #434.
- 11-15 (Zach): one ground-edit system: natural cutaways for every levelling or cut, one seam method wherever a mesh replaces the heightmap (W-174, #406). Top concern. Shipped: design note, one edit record and one hole mask plus seam builder (row 1, PR #440); natural cutaways for pass gullies, beds, pads and paddies (row 2, PR #481); river carves, falls and pool lips on the same profile, plus cave passages without pits (row 3 and W-176, PR #503); one seam builder for mouths, temples and scarps plus the seam test (rows 4-5, PRs #512, #515). fauna movement and spawns respect the hole mask (A-135, PR #525). entrance collars for temples and cave mouths (W-174 r6, PRs #520, #526) (A-135). Cave interface mesh r3 #553, collar facing fix (c6 slits gone) #563, mouth floor gets terrain grain and relief #566 (11-17); rim welds to collar foot #572 (11-17), collar feet on drawn field #577 (11-17). Next (Zach 11-17): exact seam, no more snapping: the mouth patch shares the heightmap boundary vertices, gap 0.
- 11-15 (Zach): Model Lab body builder: add or remove limbs on archetypes, limb kinds, per-segment taper, subdivision, lean, colour and material; building and dungeon set pieces in the Lab (M-06, #412-#414). Shipped: steady turntable, builder note (PR #417), in-memory builder edits (PR #418), set pieces viewable with variant/seed and a parts list (M-08, PR #435). Next: the builder, after Zach's answers. Shipped: the builder saves a new creature and assigns the biomes that own it (M-06 row 3b, PR #442; first Lab species Frostdeer, Tundra). Shipped: Lab species join herds (PR #453; Frostdeer herd in the Tundra). Shipped: per-segment material kinds (M-06 row 4, PR #459). Shipped: row 5 blueprint buildings on W-175's unit format (PR #469).
- 11-15 (Zach): placed units (stall, smithy tent) are hierarchies of standalone parts (awning, table, anvil); parts reused, one change reaches every instance (W-175, #415). Zach OK'd the parts/units proposal 11-15; row 2 moves stalls and smithy first. Zach 11-16: no rush on old buildings, but the end goal is a total conversion. Shipped: parts and units format; stalls and smithy built from shared parts, meshes unchanged (row 2, PR #462). Shipped: tents and fire pits as units (row 3, PR #465). Shipped: crate, basket and lantern head as kit parts (row 3b, PR #477). Shipped: part format, building parts, nested units and UnitLibrary (rows 4-6, PR #508). Shipped: first blueprint building in the world, a Cookhouse from cookhouse.json in human villages, with a placement guard (PR #516). Next: convert more buildings to blueprints (no rush).
- Tree variety (#313, A-113): more temperate tree kinds and tall emergents, imperfect saguaros. Shipped PR #433.
- 11-15 (Zach): build blueprint buildings in the Model Lab from kit elements, generated in game; shows the kit's limits (M-06 row 5 on W-175's unit format, #414). Shipped: Lab blueprint editor (PR #469). Shipped: kit gaps as W-175 rows 4-6 (PR #508).
- 11-15 (Zach): a map editor that loads the world in the Unity editor and edits the heightmap (M-07, #260; design note first, through W-174's ground-edit system). Shipped: editor-only window with raise, lower, smooth, flatten and a saved edits file (M-07 rows 1-2a, PR #425). Shipped: painted edits baked into the game world, edits hash in the join signature (row 2b, PR #504). Next: towns and roads refit around edits (Zach 11-15: rebuilding them is fine).
- 11-16 (Zach): entrance collars: each cave mouth or dungeon entrance gets a small terrain mesh whose outer ring matches the heightmap cut vertex for vertex and whose inner ring matches the entrance piece's frame, joined by ground-textured faces; replaces snapping grid vertices to the rim (corners left 0.25-0.32 m lips). W-174 row 6, #405. Next (world, after W-175). Shipped: temple collar #520, cave mouths #526. Row 3 walls stop at the shared mouth ring shipped #553; c6 slits left (11-17).
- 11-16 (Zach): cave fungi grow as real mushrooms, one mushroom system above and below ground (W-177, #522). Shipped #527.
- Hazy caves (Zach 11-16): keep cave haze as a look, not a bug. Some caves or styles can be foggy on purpose, and everything inside must still read through it (fungi fix #528). Shared cave haze shipped #532; audit done (haze per style: Crystal, Ice).
- 11-16 (Zach): glowing tendrils hang from cave ceilings, and a handful of set pieces react to touch (W-178, #523): one local chain sim (tendrils, vines, hung lanterns, chimes, curtains, rope bridges); networked heavy physics pieces are a separate later card. Later. Shipped: tendrils #531, set pieces #533 (roots, vines, mine ropes).
- W-181 cave climb routes (#534): rebuild the W-144 routes on the W-162 rock grades and W-178 hangings. 11-17 (Zach): certain cave walls also get the cliff kit's climbable, varied-length ledges, and world ledges (cliff and cave) feed the ledge-hang list, which today only takes debug lips. Later.
