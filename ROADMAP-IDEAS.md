# Roadmap ideas (Zach)

Zach's long-range ideas, paraphrased; one entry per idea. His own wording is on the issue named. The supervisor
keeps this file; lanes read it only when a card names a section. Before carding an entry, check the issue,
`lanes/ISSUES-MAP.md`, cards in flight and main.

Standing: the aim and its priorities are in COORDINATION.md. The layout is a test bed and may change
(`lanes/shared/world-data.md`). Core tenet: small, varied, seeded details everywhere, kept sparse.

Status words: **Shipped** (on main), **In flight** (a card or branch is working on it), **Next** (carded or
asked, not started), **Later** (idea only). Updated 11-15 against main and merged PRs.

## World and far view
- **Far views (#298).** The whole island in sight: ground to the horizon, far forest, far life. Shipped: far
  terrain ring to about 3 km (W-131), tree and shrub impostors (R-7, R-11). Next: R-22 (whole-island ground,
  far forest, weather haze, approved 11-13), R-23 (one lighting path), A-108, A-110.
- **Cliffs (#281, DESIGN-cliffs.md).** Cliffs built as the ground itself. Shipped: steep ground reads as layered
  rock (R-19), mountain tunnels (W-138). In flight: the scarp builder W-155 (big cliffs and river banks, which
  Zach liked on 11-14) on world/scarps, being landed now; walk and climb (row 3) and the shared rock wall with
  caves (W-162, #327) come after. Shelves with homes, ruins and flora are later.
- **Rivers (#301, W-153).** No kinks, dead ends or water walls. Rows 1-2 done; rows 3-6 next (one course map,
  profiles and lakes on the finished ground). Shorelines that wander (W-160, #324) next.
- **Waterfalls (#284, R-21).** One builder for every shape of fall, a subdivided curved sheet. Shipped: deforming
  water, river falls and rapids (R-9). In flight: R-21 rows 3-4, folded into W-153.
- **Lava and toxic pools (#303, W-154).** Shipped: first lava look and per-fluid underwater (PR #213). Next: W-154
  coarse crust normals and the far form.
- **Lights (#302, R-25).** No light switches on as you approach. Next: R-25 rebases the old far-light work.
- **Shadows (#286).** Shipped: one shadow darkness (R-16), steady near and high-preset sun shadows (R-20 row 1,
  R-32), blue moon shadows (R-32), no sun or cloud shadow through cave roofs (R-36). In flight: R-20 remaining rows.
- **Stylized realism (#304, R-26), foliage (#323, R-30), grass fields (#325, R-31), salt-flat cracks (#331,
  R-33), wet stone (#332, R-34, in flight), clouds from a reference shader (#333, R-35).** Next, render lane.
- **Sun moves smoothly (R-38, 11-15).** Sun position every 0.1 s as a trial. In flight.
- **Caves.** Shipped: screen-size cave LOD, cave water, dungeon texturing and collision. In flight: playtest
  fixes, tunnel variety and cave walls that fit the place (W-172 rows 5 and 7: moss only near light or damp,
  glowing fungi deep, carvings near ruins, fossils in sedimentary rock, frost, salt crust, flowstone, tide
  marks, soot near camps, roots under forest). Later: small-animal passages to secret rooms and river-carved
  tunnels (#370).
- **Floating islands (#256).** Later. Walkable, some with caves, one with a new kind of town; reached by flying.
- **World file (#260, #345).** Shipped: the baked island is saved and loaded instead of rebuilt (W-140), start
  ledger (W-168 row 1). Next: bake meshes into the shipped file (#345). Hand-editing is far off; rule 22.
- **Plant fidelity (#21)** experiments. **Cluster culling (#270)** waits for profiler numbers.

## Settlements
- **Sea folk city and turtle folk villages (#37).** Shipped: built and populated (A-103). Far town life is A-109
  (ready to land).
- **Fishing and marsh villages, stables, ladders.** Shipped (W-119, W-122, A-117, W-147).
- **Farm fields (#335, W-165), one material library (#336, W-166), towers with real storeys (#337, W-167),
  elf towns without wood (#326, W-161), wood grain and strangler roots (#330, W-163).** Next, world lane.
- **Castle walls (#254).** Later. Modular walls, towers, a gatehouse with a working, synced portcullis.
- **Port town (#255).** Later. A big pier, a boardable galleon and a busy main road.

## Folk and races
- **Playable races (#291).** Shipped (A-103): sea, bat and turtle folk; bat eyes see brighter.
- **Breath (A-102).** Shipped: works like stamina; air breathers resurface; sea folk never run out.
- **Households (#25, A-105).** Shipped: own homes, going home at dusk, family talk. In flight: household pets
  (#340, A-120). Later, after items (P-35): owned items, and theft that angers owners and guards.
- **Conversation (#25).** Shipped: personalities, a conversation API, reactions to an animal player (A-93),
  hermit chores and talk (A-106), the player talk system (P-28). Later: dialogue options, a hand-editable text
  archive reloaded live.
- **One population (#298, A-110).** Next. Far figures are a town's real people; nobody swaps as you approach.
- **Skills and attributes.** Shipped first stats (P-28). Grows as features need it.

## Creatures
- **Riding (#278, A-104).** Shipped: any creature rides one at least 50% heavier, players ride players, folk ride
  horses out of town stables (A-117).
- **Climbable colossi (#276, A-107).** Shipped: graded climbable sections, carried as the colossus walks.
- **Sand and snow worms.** Shipped: breaching snow worm and sand worm colossi (A-84, Z-28).
- **Climbing (A-101).** Shipped: one climbing model, ladders, marked walls, ledge hang. In flight: ladders for
  creatures (A-126); cave routes wait on W-144.
- **Fishing (A-100, P-33).** Shipped: catch tables, fish notice the bobber. In flight: player fishing (P-33).
- **Individual builds and gaits (A-124).** In flight.
- **Gold variants.** Shipped: 1 in 400.

## Dungeons, items, quests
- **Dungeons (#24).** Shipped: texturing, bigger caves and ruins, a dungeon state API, collision (A-91 row 1,
  A-95). Next: A-91 rows 2-4; set pieces (player lane), then spawn sockets (animal lanes).
- **Items and inventory (#26).** Shipped: first items and doors (P-28), pocket reach (A-114). In flight: hand
  actions and weapons (P-38). Next: one item definition and saved state (P-35, design first).
- **Quests (#27).** Shipped: first quests (P-28). Later: more objective types, tracking, dungeon quests.

## Seasons
- A generic calendar: day-of-year counter, configurable year length and season count, synced. Values follow
  curves over the year and never jump at a boundary.
- Global: sun and moon arcs and day length change through the year.
- Per biome, a profile by day of year: temperature offset, weather odds, flora look (colour, leaf drop, bloom,
  snow, dormancy), fauna spawn numbers, water (desert rivers dry most of the year and fill in a monsoon; frozen
  lakes, spring floods, shrinking ponds).
- Animals and folk never read the season. Their behaviour changes only through light, temperature and storms
  (builds on A-71).

## Fire
- One shared fire effect with a colour setting, used by everything that burns; Ember Forest burns one seeded
  colour per night. Shipped (#131) with 4 colours of the 7 asked for (orange, golden, silver, violet, emerald,
  crimson, black). Zach wants further tweaks later.
- Effects as reusable systems (11-14): electricity, steam, smoke, sparks, gushing water and wind each one shared effect like fire, so new places reuse them as systems grow. R-29, rule 30.
Foliage realism (Zach 11-14): crowns as leaf clusters (cut-out cards, layering, bent normals) instead of solid toon shapes; #323, R-30.
- Shorelines wander instead of ruled lines: lapping, noise lift, ragged wet band (W-160, #324).
- Continuous grass swathes without bald spots; grass ground textured with blades, dirt speckled and grainy (R-31, #325).
- Elven cities have no wood: signs, fences and props in stone or metal (W-161, #326).
- Cliffs and caves share one rock wall look, climbable grades and the ground blend at cave mouths (W-162, #327).
- Blue moon casts the shadows on blue-moon nights, so its blue light reads as coming from it (R-32 row 3, #328).
- 11-14 (Zach): procedural wood grain on planks and static strangler-fig roots on swamp trunks and cave walls (W-163, #330).
- 11-14 (Zach): caked, cracked dry ground on salt flats and cracked bark (R-33 #331, W-163 row 3).
- 11-14 (Zach): wet-stone look trial on in-game stone (R-34 #332).
- 11-14 (Zach): explore a reference cumulus shader for cheaper or better clouds: R-35, #333.
- 11-14 (Zach): farm fields outside towns, crops for every biome, e.g. snow potatoes, agave-like ferns (W-165, #335).
- 11-14 (Zach): one material library: every built piece states what it is made of, families share look rules, kinds vary colour (W-166, #336).
- 11-14 (Zach): towers sized so a stair and rooms could fit, storeys readable from outside (W-167, #337).
- 11-14 (Zach): household pets; folk pet animals by personality; children carry small animals inside (A-120, #340).
- 11-14 (Zach): a rider and mount should share one size for LOD (A-119, #339).
- 11-14 (Zach): no one walks through a campfire; every fire gives heat that comfort uses (A-121, #342).
- 11-14 (Zach): move more generation into a one-time bake shipped with the game so starts are faster (W-168, #345).
- 11-14 (Zach): gear, dropped items and animal parts (horn, shell, hide) share the material library (W-166, #336).
- 11-14 (Zach): one material library for everything, fur, feathers, scales and skin included, so furniture and gear can use them later (W-166, #336).
