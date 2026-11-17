# Playtest list (for Zach)

The supervisor closes issues once they're merged and checked by the agents. This list collects what's worth a look
when you play. Reopen any issue that's wrong; otherwise just delete its line. Newest at the top.

| Issue | What to check | Where | Shipped |
|---|---|---|---|
| v0.14.0 release | Everything in the notes: talking to folk, gear, quests, road tunnels, bigger dungeons, beacon towers, new water and storms, the load veil after jumps. Hosting from this build shows a Windows firewall prompt the first time | https://github.com/Skeleman/Punchy/releases/tag/v0.14.0 | 11-12, tag on rc/v0.14.0 56b15b8c |
| #287 load order and veil | After a long jump or a fresh start, trees and buildings come first, then small plants; a short veil covers the pop-in (up to 4 s). `-noveil` turns it off | Any long jump | 11-12, #288 #289 |
| #282 white bloom circles | No white dot on tree tops or anywhere else at Ultra, the hilltop at sunset especially; grass looks the same | Hilltop -3460,2425 at 18:00, Ultra | 11-12, #283 |
| Marsh wildlife, colossi | Frogs on lily pads, a heron in the shallows, dragonflies darting low at marsh folk villages; magma, ember and spore colossi glow at night; fewer hitches when the plant, salt and magma colossi appear. New pins Landmarks_V3 112 A955AFE0, Fauna 83902658 | Foggy marsh villages; colossi at night | PR #193, 2026-11-07 |
| Marsh folk | Stilt villages over the water: reed huts, plank walks, a smoke hut; drying frames, eel traps, totems; a nest raft of eggs with a hatchling behind the smoke hut | Foggy marsh villages | PR #192, 2026-11-07 |
| #39 tunnels | Roads go through tunnels under steep crests, with a stone portal at each end | Hilly roads between towns | PR #178, 2026-11-06 |
| Camps, igloos, temple | Leather tents and ramadas in camps; igloos sit into slopes; the sunken temple has sandstone blocks and torch glow, with no plane through the rooms | Camps, igloo villages, sunken temple | PR #170, 2026-11-06 |
| #40 mire walker | A mangrove-rooted giant with fog eyes and a lantern looms out of the marsh mist and walks a slow loop | FoggyMarsh, v3 near -1804,145 | PR #169, 2026-11-06 |
| #40 snail | A huge cinder snail creeps a slow loop through the Ember Forest; its eyes and shell seams glow from far off | EmberForest | PR #168, 2026-11-05 |
| Cave folk | `-incave` lands you in the bat folk chamber on v3; pygmies and bat folk live in their caves | v3 caves, `-incave` | PR #168, 2026-11-05 |
| Cave mouths | No rim where a cave mouth meets the hillside; the ground round it has the cave's rock grain | Any cave mouth | PR #167, 2026-11-05 |
| Far shadows | Tree shadows reach 120 m and are darker. If still too light, launch with `-shadowcachedark 0.8` | Forest from a hill | PR #167, 2026-11-05 |
| Far fires | Burning forests glow from far away at night; embers match the fire's colour | Forest fire at night | PR #167, 2026-11-05 |
| Colossi | The poison plant is twice as tall and sways; the heron's crystals stay on the ground; the saguaro stands on roots; the golem is dark basalt | v3: plant at -goto -53,34,516 | PR #166, 2026-11-05 |
| Colossus paths | Animals walk round colossi instead of into their legs | Golem ring, v3 -goto 729,-335 | PR #166, 2026-11-05 |
| v3 default | New builds start on the v3 island with no flags (`-island v2` still works) | Any launch | PR #165, 2026-11-05 |
| #39 roads | Roads run unbroken up steep slopes now, cut into or banked up on the hillside | Hilly roads | PR #165, 2026-11-05 |
| Waterfalls | Falls show falling white and grey sheets, not flat glowing white. Try fall 74: -goto -2167,3,3691 | v3 at noon | PR #165, 2026-11-05 |
| Wharf | Fishing village 1 now has its wharf by the river mouth (not in the 11-05 playtest build) | Fishing village 1 | PR #164, 2026-11-05 |
| #40 heron | A glass heron wades the dunes, rests, and stabs at the sand; it turns to look at you | GlassDesert | PR #163, 2026-11-05 |
| #40 stag | A huge stag grazes between the trees; its antler lanterns glow brighter at night | SparklingForest | PR #163, 2026-11-05 |
| #39 roads | Roads turn from dirt to cobblestone at a town's edge; ruts, gaps with grass, the odd missing stone | Any HumanVillage road | PR #162, 2026-11-05 |
| #39 squares | Flagstone square with a kerb and worn paths round the town's pond or fountain | HumanVillage, elf town | PR #162, 2026-11-05 |
| Buoys | Buoys off the wharf end float clear of the stilt-house piles | Fishing village 2 | PR #162, 2026-11-05 |
| Folk heat | Desert and snow folk are out working at 8 h; at midday most stand in the shade of the eaves | Desert HumanVillage | PR #161, 2026-11-05 |
| #40 tortoise | The mesa tortoise walks a slow loop and turns to look at you | MesaCanyonlands | PR #161, 2026-11-05 |
| Gulls | Some gulls sit on the buoy masts off the wharf end | Fishing village 1 | PR #161, 2026-11-05 |
| Shadows | Far trees still look right with the new default (cheap + cached far shadows); no shadow pops while walking | Forest, fishing village 1 | PR #158, 2026-11-05 |
| #80 | Ruin brick walls: broken courses, fallen bricks, moss and sand | Any ruin | PR #158, 2026-11-05 |
| #70 | You can't run through the flat rock chunks, and you can stand on the low ones | Electric Wastelands | PR #158, 2026-11-05 |
| #8 | Folk greet, eye or avoid you depending on your race | Any village, try 2 races | PR #157, 2026-11-05 |
| #131 | Fire look (you plan to tweak it later) | Ember Forest at night | PRs #152, #153 |
| #62 | Mantrap bite still shoves you back; colossi and mantraps appear as before after world load | Dunes, Stone colossus area | PR #177, 2026-11-06 |
| #40 | New colossi: Tundra snow worm, Temperate walking oak (rope swing, owl, saplings in its footprints); mire walker fog visible by day | Tundra, Temperate, Foggy marsh | PR #180, 2026-11-06 |
| #40 | New colossi: fungal puffball (glowing spores), charred treant (ember trail), ice mammoth (sheds ice chunks) | Fungal forest, Ember forest, Glacier | PR #183, 2026-11-07 |
| #40 #69 #70 | Round dented salt crystal ball; biome hazard houses (ash, snow, storm, sand) in harsh-biome towns; magnetic drift mounds no longer hang over steep slopes | Salt flats, Ashlands OgreCamp, Tundra dwarf towns, Electric wastelands | PR #184, 2026-11-07 |
| #40 | Ice mammoth now walks Glacier slopes; new sky whale floats over the Mountains (spouts on updrafts, sings) | Glacier, Mountains | PR #185, 2026-11-07 |
| #36 | Fishing villages: stilt houses and docks on the water, boardwalk pier networks, per-village trades (crab pots, nets, drying racks), rope coils, gulls | River mouth villages | PR #186, 2026-11-07 |
| #40 | Mire walker now wades its marsh loop with splashes; new hermit crab colossus with an islet and lighthouse on its shell (lamp lit at night) | Foggy marsh, Ocean shore | PR #187, 2026-11-07 |
| #37 | Sea folk lake village: central spire, glowing rune stones, snaking kelp, sunken boats, giant clams with pearls, bubble vents; boardwalks now ramp on steep banks | Sea folk lake (dive), fishing villages | PR #188, 2026-11-07 |
| #21 #37 | No haze edge from peaks near snow; sea folk buoys, moored raft, bigger wrecks; leafier shrubs and richer flowers (daisy, buttercup, poppy) up close | Peaks in snow weather, sea folk lake, meadows | PR #189, 2026-11-07 |
| #40 #37 | Magma serpent colossus in its own crater (Ashlands); fish schools round the sea folk domes that scatter from you, sturgeon on the lake bed | Ashlands, sea folk lake | PR #190, 2026-11-07 |
- 2026-11-09: closed #60 (Decide priority table, A-82 de458d0). Retitled and updated #22 (underwater sound only), #24 (dungeon polish), #26 (item models, equipment), #27 (quest tracking, dungeon quests); #28 stays on hold; #85/#78 close when P-22 merges.
- 2026-11-09: P-22 merged (#229): roof undersides lit like shaded walls; #85 small-body cave camera closed; #78 display modes closed (already in DisplaySettings).
- 2026-11-10: R-7 merged (#233): far shrubs as octahedral impostors, shrub reach 260 -> 520 m (+0.0-0.34 ms); shrubs now visible from 300 m altitude. Atlas trim (140 -> ~39 MB) follow-up running.
- 2026-11-10: R-7 row 5 merged (#234): impostors baked only for the 60 models that can become one; atlas 140 -> 39 MB, bake 14.9 -> 2.1 ms, no look or ms change.
- 2026-11-10: R-8 merged (#235): cheaper sky. Skybox -54% at noon, -39% at night; storm clouds -7.5% (missed the 15% target, kept the clean version). No visible change.
- 2026-11-10: W-130 merged (#236): far shrub impostors drew as magenta slabs on D3D11 (shader compile error since #233); fixed, impostors now match the meshes.
- 2026-11-10: A-92 merged (#237): lying animals keep legs above ground and stand before moving (#231 closed); fur, near fur outline and smoother joints on by default, `-nofur`/`-noshells`/`-noseams` to compare (#230 closed). Check: a wolf or rabbit up close; a horse lying on a slope.
- 2026-11-10: A-93 merged (#238): folk have seeded personalities and answer who/how/work/town/news/favour; as an animal they pet, fear, shoo or marvel at you. No talk window yet (P-26, player account); `dialogue.json` `conversation` is hand-editable (see its `_readme`).
- 2026-11-10: R-11 merged (#239): far trees draw as impostors past 30 px (-0.1 to -0.26 ms, no look change). Rocks stay on meshes (impostors cost more). Longer tree reach waits for a bigger far terrain ring. Check: a forest from a hill and from 300 m, walk toward it for pops.
- 2026-11-10: A-94 merged (#240): flying birds leave one faint breath dot, no trail; the ice mammoth and snow worm breathe big slow puffs only in the cold. Check: a condor flying in Tundra snow; the mammoth up close (puff may look bright; easy to soften).
- 2026-11-10: Zach OK'd fur, near fur outline and smoother joints on by default (A-92) and the mammoth breath as is (A-94).
- 2026-11-10: W-131 merged (#242): at High the far ground reaches ~3 km (was ~2 km), so flying high shows much more land before the haze (+0.1-0.35 ms). Check: fly at 300 m on High and look at the horizon for seams or pops; switch presets a few times mid-flight.
- 2026-11-10: A-95 merged (#243): dungeon chests and stand-ins are solid (stand or perch on top); no falling through or getting stuck in caves, the buried ruin or the sunken temple (walk test: 0 falls, 0 stuck). Check: a rabbit and a crow round the temple's steps and chests. Camera still clips rock behind small bodies (separate).
- 2026-11-10: A-96 merged (#244): no more white sparkles on running rabbits or cave birds (NaN in the fur bump spread by bloom); fur looks identical. LiveFolk_Answer test fixed. Check: a rabbit running at dusk, a bird in a dungeon room.
- 2026-11-10: A-97 merged (#245): each animal part has its own surface (fur short/medium/long, feathers, scales, wet eyes, matte hooves and horn); wolf, dog and bear paws are coat-coloured with toe grooves, pads underneath and claws, no more dark shoes. Fur and feet detail switch by on-screen size. Fauna pin now AD723954 (look bytes only). Check: a wolf mid-stride up close, a lizard and a bird in sun; `-nosurfaces` for the old look.
- 2026-11-10: A-98 merged (#246, closes #22): under water every sound is muffled and lower, with a low hum, bubbles and the odd creak; above water, sounds behind walls and hills are muffled and come round through doors and cave mouths. Check: dive in the sea folk lake; stand behind a house while a folk talks, then step round the corner.
- 2026-11-10: Zach: the Ember Forest may stay brighter than a town at night (R-10 4c, kept as is).
- 2026-11-10: #212 merged: plant stems root into the ground, bark and leaf gloss; bark grain fades by screen size (plant LOD was already screen-size based; the 3D distance fix came earlier). Check: a meadow and a forest floor up close.
- 2026-11-10: #213 merged: lava looks molten; under water each fluid has its own fog and colour (sea, lake, swamp, cave water), light shafts and motes; storm swells. Check: dive in the sea, a lake and a swamp at noon; a storm at the coast. Underwater sound (A-98) still works.
- 2026-11-10: #248 merged: player builds compile again (dungeon walk debug tool was editor-only). No visible change.
- 2026-11-10: #249 merged (A-91): dungeons get dressed stone, wood and iron with real props; caves about 2x with gentle main paths; ruins about 4x (two ruins now); dungeons have names and a cleared state for quests. Check: walk a cave and a ruin end to end, look for stuck spots or steep paths.
- 2026-11-10: Zach on the R-12 storm cloud before/after: "Clouds are looking MUCH better". Merge once ground shadows follow and the bench passes.
- 2026-11-10: #250 merged (A-99): creature extras (torch flames, bat colonies, bee swarms, footprints, worm marks, wake ripples, critters, other players' breath) now fade by how big they look on screen, not fixed metres. Same distances as before at 1080p and default detail; reach further at higher Detail or zoomed views. Check: torches in a village at night, a hive's bees, nothing popping.
- 2026-11-10: #251 merged (R-12, closes #241 and #247): storm clouds have ragged, heaped rims instead of smooth pills, and their ground shadows match. Rain no longer greys out nearby ground (a table was drawn twice, so every distance held the whole rain shaft; the lone-oak grey wall was the same bug). Rain shafts are thinner and streaky, with gaps, following the cloud. Check: a storm at noon and at night from a hill, and standing in rain.
- 2026-11-10: Zach on the #251 rain before/after: "The rain looks much better now as well :)". Density stays as merged.
- 11-10: #252 merged (M-05 rows 1-3): Model Lab editor window (Punchy > Model Lab), tunables asset empty so no look change. Try: load Wolf, drag Legs, scrub run.
- 11-10: #253 merged (R-9): fine water sheet near the player, river falls that curl and ripple, whitewater on rapids, no zigzag foam far from origin. Leftover: jagged streaks at river mouths.
- 11-12: #232 closed (water deforms under the player): landed in #253 (R-9). Cost/coverage follow-ups on R-15 (#269).
- 11-12: #279 merged (W-138, closes #257): road tunnels go through ranges, mountains and big hills instead of small bumps; C0a is a 1070 m bore under the border range with a waystation at each end. Roads now reach every mine, some caves and the sea folk landing, with loops and a ruined-bridge break. Inside: cobbled floor, cut-stone walls, dim lanterns, a shrine and a marker stone. Check: walk C0a end to end, and the roads to the sea folk landing.
- 11-13: #295 merged (A-103): turtle folk live in the mountain igloos and sea folk in the lake city, with a few sea folk grumbling on the surface; children and elders doing small things; sea, bat and turtle folk are playable; bat eyes with glasses and glow goggles. #291 closed. Check: the TurtleVillage at dusk, the lake city bed, -pickspecies BatFolk at noon and in a cave.
- 11-13: #296 merged (A-105): every house has a household with named members; housed folk now differ in look, traits and name from before (placement unchanged). Folk go home to their own door at dusk, children earlier. A new "How's your family?" talk topic names kin, and the dead as lost (host only). #25 stays open for the player half (owner tags, theft). Check: a town at dusk, ask a villager about their family.
- 11-13: #297 merged (A-104, closes #278): any creature rides one at least 50% heavier; folk ride animals in daily life and carry pets on their shoulders; players ride creatures and each other (interact to get on, interact or jump to get down, the ridden player keeps control). Network layout changed: old builds can't join. Check: ride a dog, then host and ride your friend.
- 11-13: #300 merged (R-16, closes #272): tree shadows keep one darkness from far to near (no dark brown band at 50-120 m, no lit gap), and far shadow edges blend instead of stepping. Check: walk towards a lone tree at noon from 300 m; stand still on a hill and watch far forest shadows for a minute.
- 11-13: #305 merged (A-106, closes #69): folk in harsh biomes do work that fits (ash sweeping, snow shovelling, sand clearing); the hermit works its biome's chore and mutters to itself; on a joined client, family speak of a member as lost after the client saw it die. Check: visit a snow or ash settlement by day; talk to kin after a death while joined to a host.
- 11-13: #306 merged: the build's version number now reads 0.14.0 (was 0.1.0). Nothing to check in play.
- 11-14: #307 merged (A-107 rows 1-2, #276 stays open for row 3): stone colossi have graded climb sections; climb a flank column, stand on the back or head, and ride a walking colossus (it shakes you off when you lose grip). Climb cracks and ledges show chalky on dark colossi. Nothing new on the wire. Check: climb a walking colossus with a friend joined and ride it together.
- 11-14: #310 merged (closes #309): far trees no longer turn into big lit squares after a shader changes while the game runs in the editor. Players never saw it. Nothing to check in play.
- 11-14: #318 merged (A-112): animal pairs (young and parent, pet and owner) find each other with a local search, and riding no longer stalls each frame while friends are in the game but nobody rides. Nothing new on the wire. Check: join a friend, both on foot near herds; then one rides a horse.
- 11-14: #319 merged (R-27): tree shadow cache takes its slope before skipping pixels (fewer specks along cache edges). Check: dusk in a forest, watch shadow edges at 50-150 m.
- 11-14: #285 merged (steep terrain reads as layered rock, High up); #281 stays open for real overhangs and ledges.
- 11-14: #347 merged (A-114): folk reach to a hip pocket, belt pouch, coat chest or over the shoulder to put an item away or take one out; drawn on each machine from the held item, nothing new on the wire. #314 closed. Check: watch a lamplighter or miner swap items; play as sea or turtle folk.
- 11-14: #348 merged (W-168 row 1): a one-line start report ([start]) in the log at world ready; no visible change.
- 11-14: #349 merged (A-118): four-legged animals lie on folded legs (hoofed ones tuck forelegs, hind legs to one side); no leg sticks out behind. #329 closed. Check: horses, deer and cattle lying in a field, side and front.
- 11-14: #350 merged (R-36 row 1): cave interiors no longer catch sunlight or drifting cloud shadows through the roof; mouths and the outside unchanged. #338 stays open for later rows. Check: a shallow tunnel on a cloudy noon, walls should stay still.
- 11-14: #351 merged (Doors.IsOpen, player lane 11-10): read-only door state; no visible change until A-122 (#352) wires it into sound.
- 11-14: #353 merged (R-20 row 1): at High and Ultra, sun shadows use a wider filter and a sharper 4096 map, so the small hop at each sun step is softer. The fix for crawling shadows is the stepped sun (#343, every preset); this is polish on top of it. Medium and Low unchanged. Zach 11-14: the noise is gone; he weighed faster steps (0.1 s, 0.05 s) and kept 0.4 s, because faster steps still show sun artifacts. #286 stays open. Check: High, stand still near the stable or a house at mid-afternoon and watch shadow edges 10-60 m out.
- 11-14: #355 merged (R-32 row 2): grass blades darken in the same shadows as the ground under them, including far shadows, and shaded blades shouldn't speckle. Check: High, a meadow near trees mid-afternoon; grass in tree and cliff shadow 20-100 m out should match the ground, with no bright blades in shade. #328 stays open (rows 3-4: blue-moon shadows).
- 11-14: #363 merged (R-32 rows 3-5): about one night in ten the blue moon is full and casts the night's shadows, handed over with a smooth fade; other nights the big moon casts as before. #328 closed. Check: wait out a few nights (or -bluemoondays 0 at night) and watch blue-moonrise for any jump in the light.
- 11-14: #364 merged (P-32): menus move up/down/left/right by layout. v0.15 playtest logged in FEEDBACK-2026-11-14.md; cards W-172, R-38, A-126, A-127, P-37 (#365-#369), ideas #370.
- 11-14: v0.15.0 released (rc 7ed1b063, #371 merged the version into main). Hand controls carded as P-38 (#372).
- 11-15: catch-up of verified branches that were never PR'd (rule 31). Merged: #373 A-109 far town figures fade by screen size; #374 A-115 lamplighter lights each town at dusk (street lights and lamp glass follow the rounds); #362 A-116 notice board; #375 A-121 fires give heat, walkers go round them; #376 A-122 doors and sound; #377 W-155 scarps (cliffs and river banks as ground); #378 W-164 level garden beds; #379 W-145 no cave-mouth pop; #380 W-148 no water discs; #381 W-169 puddles across chunk borders; #382 R-17 sea swell no longer tiles. Closed #273 #275 #277 #280 #316 #317 #334 #338 #342 #352; #308 closed as a duplicate of #286. Still landing (conflicts after these merges, back with lanes): A-123 birds and dogs, A-124, A-113 saguaro and trees, A-108, A-110, R-25 far lights, R-15, R-28, R-23, W-150, W-168, R-21, bench 75.
- 11-15 (Zach's review of open issues): closed #24, #25, #26, #27, #37, #261, #266, #276, #281, #286, #312 as addressed, each with a note naming where the remaining work is tracked; #267 closed as a duplicate of #327 (one climbing method for cliffs and caves). Rule 32 (issues stay true) and the punchy-sync audit added. W-166 row 1 audit confirmed; row 2 next.
- 11-15 (backfill; merged 11-12 to 11-14 but never logged here): #264 W-134 cave water; #268 W-140 baked island saved and loaded; #271 W-135/W-136 screen-size cave LOD and contact darkening; #290 A-100 fish catch tables; #292 A-102/A-103 sea and turtle folk, diving and breath; #293 W-147 ladders; #294 A-101 one climbing model; #299 far-view docs; #341 A-117 town stables; #344 A-111 row 2 hermit talk; #346 bake determinism.
- 11-15: #383 merged (A-126 row 1): ladders climb about twice as fast (4.3 m roof ladder 6.4 s -> 2.9 s up); walls and trunks unchanged. #367 stays open for rows 2-4. Check: climb any town ladder. Decision (supervisor, Zach asleep): CreatureBlob.Style parity bug fixed by scrambling before the modulo, option (a). Odd hair colours and part styles now appear; every creature's look changes once and the fingerprint is re-pinned (layout and looks free per 11-13).
- 11-15: #384 merged (W-172 row 1): rice paddies only form on gentle ground, so the spires and water strips hanging on slopes are gone (spikes 180 -> 32). #365 stays open for rows 2+. Check: the hills around -65,57,40. #385 merged (A-123): goblin and folk arms meet the torso, dog faces read from a few metres, birds look feathered. #356 closed. Check: a dog and any bird up close.
- 11-15: #354 (R-37) closed. Zach saw no difference between the bias pairs; the sun shadow bias stays 0.1/0.5. Edge stepping in motion goes to R-38 (#366). rc-test/* and wip/* archived (Zach OK).
- 11-15: PR #386 W-150 cliff kit (#281 closed earlier): kit pieces and ledges on sea cliffs and mesas, on the finished ground. Check: Sea cliffs and mesa faces show stone pieces and climbable ledges.
- 11-15: PR #387 A-124 row 1 (#358) individual builds; PR #388 even style draws; PR #389 A-120 household pets (#340 closed). Check: animals of one kind differ in build; hair and limb styles spread evenly; folk pet animals, and children carry the household pet inside, then it comes back out.
- 11-15: PR #390 A-127 (#368 closed): turtle robes swing with the legs, sea folk stand upright on land, wading folk keep their breath. PR #391 test harness spawn wait (tests only). Check: turtle folk walking show no legs through the robe; sea folk on land stand straight.
- 11-15: PR #394 A-128 (#392 closed): smith aprons reach the knee and ogre capes cover the back, both clear of the legs. Check: walk beside a smith and an ogre guard; no legs through the cloth.
- 11-15: v0.16.0 released (main 40478293 + version bump, PRs #362, #373-#394). Checklist release/checklist-v0.16.md.
- 11-15: v0.16 playtest notes from Zach carded: riding #396 (P-40); menus, Start/Select #397 and facing prompts #398 (P-39); buildings #399, cuts #403, sunken temple #405, water strips #301 (W-173); tunnel hitch #400 (bench 96); 3D distance #401, glow and darker nights #402, igloo light pop-in #302, water rebound #269 (R-39); dune palms #404 (A-130).
- 11-15: Zach: ground reshaping is the top recurring problem; W-174 (#406) carded, folds in #403 and #405. A-130 to reuse W-126's clearing mask.
- 11-15: Zach picked R-34's wet look and wants the stone function as the base rock pattern, with wetness only adding shine (R-34 row 2, #332).
- 11-15: Zach: put fishing on main now with rods easy to get; he playtests it there. P-41 (player, first on resume) lands take-items then fishing (#266).
- 11-15: merged #407 (W-168 r2, #345), #408 (A-125 r1, #360), #409 (R-38 rows 0-4, #366), #410 (R-20 r4), #411 (R-21, closes #284).
- 11-15: merged #416 (A-129 rows 1-3, closes #393): live-reloading dialogue archive; options design note.
- 11-15: merged #417 (M-06 rows 1-2, closes #412): Model Lab turntable holds still while the model walks; body builder design note.
- 11-15: merged #418 (M-06 row 3, #413): Model Lab body builder edits a species in memory (segments, taper, colour, lean); fingerprint unchanged.
- 11-15: merged #419 (P-33 rows 1-3, #266): player fishing on main: rods, cast, bite, reel. Feel tuning waits on Zach's playtest.
- 11-15: merged #420 (A-131, #25): folk talk lines offer replies that branch; guard, hermit and innkeeper have a work exchange (UI to show replies comes next).
- 11-15: merged #421 (A-130, closes #404): desert biomes get palm and palo verde groves with clearings; fingerprint 9EFF3BCF.
- 11-15: merged #422 (P-40, closes #396): you dismount when you no longer fit the mount; jump-off tested.
- 11-15: merged #423 (R-25 row 3 #302, R-38 #366 closed): town lights stay steady as you walk in from 1 km; nothing switches on near you. Smooth sun step.
- 11-15: merged #424 (profiler fix, job 42): -vsync and -maxqueued launch flags for benches.
- 11-15: merged #425 (M-07 rows 1-2a, #260): Punchy > Map Editor in the Unity editor; raise, lower, smooth, flatten brushes with undo. Edits don't reach the game yet.
- 11-15: merged #426 (W-166 rows 2-3a, #336): every building and prop uses one wood with grain (planks that were flat now show it); stalls, benches, signposts, hitch rails and palisades are a little lighter far off. Elf pieces are plaster.
- 11-15: merged #427 (R-34 row 2, #332 closed): wet stone is the base rock. Cliffs, boulders, stone walls and cave walls look glossy and darker in rain, by shores and spray, and in damp caves.
- #428 fauna/far-life (A-108 row 2, #298): far herds drawn by screen size; island cache Format 4 means one rebake on first launch.
- #429 fauna/playtest-1114 (A-126 rows 2-4, #367 closed): ledge pull-up with planted hands, shimmy prompt, steer ridden animals.
- #430 fauna/one-population (A-110, #298): one population, far figures are a town's real people.
- #431 fauna/swimmer-droplets (A-125 row 2, #360 closed): dolphin and fish splashes throw droplets, at most 4 at once.
- #432 render/playtest-1115 (R-39; #401 #402 closed): glows and flames hold on a low glide, Zach's glow option 3 with darker night haze, igloo windows lit at every distance, softer wading rebound (#269).
- 11-15: #395 merged: release v0.16.0 (bundleVersion 0.16.0). Check: title screen shows 0.16.0.
- 11-15: #433 merged (A-113, #313): tulip tree, white pine, willow, beech and fir in temperate woods; saguaros with imperfections. Fingerprint pin 0654C913. Check: walk a temperate wood, look for tall trees above the canopy.
- 11-15: #435 merged (M-08, #414): Model Lab loads buildings and cave rooms as set pieces, with variant/seed fields and a parts list that highlights a group on hover. Check: Model Lab > Set Piece, load a cave room, hover a part row.
- 11-15: #436 merged (hotfix): TestHooks.Options.cs editor-guarded so player builds compile again. Check: a player build succeeds.
- 11-15: #437 merged (W-166, #336): plaster walls, brick chimneys and smithy stack with recessed mortar, igloo ice blocks with snow joints. Check: a town at low sun from 3 m, then an igloo.
- 11-15: #438 merged (A-132, #25): the innkeeper works its inn's counter by day, wipes it and stacks mugs when idle; a/an fixed before dialogue slots. Check: visit an inn by day and talk to the innkeeper.
- 11-15: #439 merged (R-25 rows 5-6, #302): town windows and lamps keep their warm colour from 1 km instead of white dots; lava has its own far light. Check: fly 1 km from a town at night, then walk in.
- 11-15: #440 merged (W-174 row 1, #406): design note for one ground-edit system (docs/design/DESIGN-ground-edits.md). Nothing to check in game.
- 11-15: #441 merged (P-43, #396): a ride keeps up when the rider or ridden player changes body mid-ride, and the rider gets down once it no longer fits. Check: ride a player, then change either body.
- 11-15: #442 merged (M-06 row 3b, #413): the Model Lab saves a new creature with home biomes; first Lab species Frostdeer roams the Tundra. Fingerprint now 0206544F. Check: Model Lab > Save as new creature; visit a Tundra.
- 11-15: #443 merged (A-125, #360): fast dolphins leave spaced, lighter wake rings. Check: swim beside dolphins.
- 11-15: #444 merged (A-108 r4, #298): far eagle over summits, dust behind far herds, fish rings on far lakes at dawn. Check: dawn at a lake, look across.
- 11-15: #445 merged (#352): audio no longer reads a released terrain copy after a rebuild. Check: rebuild the world in play, listen near doors.
- 11-15: #446 merged (P-31): dev flags -netsim (lag stage), -rttlog and -rejoin for multiplayer tests. Check: host plus joiner with -netsim 150,30,2; joiner RTT about 300 ms.
- 11-15: #447 merged (A-108 follow-up, #298): dawn/dusk lake rings no longer rise in step across next-door patches.
- 11-15: #448 merged (A-133, #336): creature gear wood and metal use the shared material library (spear shafts show the new grain).
- 11-15: #449 merged (A-108 follow-up, #298): sky birds in next-door patches no longer fly over in step.
- 11-15: #450 merged, P-38 (#372 closed): take items from folk, per-item block (lantern held high), shared gear kits, eyewear as a head item. #259 rejoin still crashes (terrain mesh job on leave), world lane.
- 11-15: #451 merged, W-172 rows 2-6 (#365): cave water source, tunnel mouths and sides, tunnel variety, horse-size passages, camp layouts. Fauna fingerprint E764AAEC, landmarks 117 689D5348.
- 11-16: #452 merged, W-166 row 3 (#336): built stone (ruins, fountains, forums, cave linings) and creature cloth and bone use the material library.
- 11-16: #453 merged, M-06 row 3b (#412): Lab species such as Frostdeer form herds in their biomes. Host+joiner check owed in the next playtest build.
- 11-16: #454 merged, R-23 row 2 (#298): one surface lighting path for terrain and built pieces (job 82 pass).
- 11-16: #455 merged, R-15 rows 1-3 (#269): shader audit fixes incl. WaterPatchSystem; row 4 waits on bench job 62.
- 11-16: #456 merged, W-166 row 4 part (#336): sunken temple walls and flagstones use the shared cut stone.
- 11-16: #458 A-124 r2 (#358 closed): each person walks in their own style (even, heavy, brisk, ambling, a few limp).
- 11-16: #459 M-06 r4 (#413 closed): Model Lab sets a material kind per creature segment (Body, Cloth, Leather, Metal, Wood, Bone); game looks unchanged until an asset uses it.
- 11-16: #434 P-39 (#397 #398 closed): menus on d-pad, Start/Select split with field menu tabs, nearest Use target in front wins.
- 11-16: #461 W #259 fix (#259 closed): rejoin no longer crashes; terrain jobs finish before a world swap.
- 11-16: #462 W-175 r2 (#415 open, rows 3+): stalls and smithy are built from shared standalone parts; they look the same.
- 11-16: #463 W-172 lava half-row (#365 open, row 7+): a volcano cone now stands in the Ashlands; one bone graveyard there is gone.
- 11-16: #464 A-119 (#339 closed): a rider and its mount now change detail and vanish together at distance.
- 11-16: #465 W-175 r3 (#415 open, row 3b+): tents and fire pits are built from shared parts; they look the same.
- 11-16: #466 P-45 r1-3 (#24 open, spawn sockets next): debug `-place piece:hall|deadend|pool` drops you at a hall, dead end or pool; no change in play.
- 11-16: #467 rule 35: `-port N` launch flag (default 7979) and `-join ip:port` now works (it fell back to solo before).
- 11-16: #468 P-42 (#25): talking to folk lists their replies; pick one, and offers get Accept/Decline.
- 11-16: #469 M-06 r5 (#414 closed): Model Lab builds blueprint buildings from shared parts and saves them (example: cookhouse); editor only, no change in play.
- 11-16: #470 #24 phase 2 r1: dungeon spawns design note (docs only); r2 near-player dungeon creatures next.
- 11-16: #471 W-166 r3 tail (#336): terrain includes the library stone directly; no look change.
- 11-16: #472 P-46 (#24): temple torches no longer in low halls, clutter kept off chest spots; temple socket hash re-pinned 2D2BFAEB.
- 11-16: #473 P-47 (#24): cave and ruin chests open and sync like the temple chest (dungeon, type, socket ids; 89 chests, no clashes).
- 11-16: #474 P-48 (#24): debug dungeon walk no longer times out on stacked ruin and temple floors; cave pits filed as #475 (W-176).
- 11-16: #476 W-166 r4 (#336): ground items and the salt tower use the material library; colossus shader reads a family channel.
- 11-16: #477 W-175 r3b (#415): crate, basket and lantern head are shared kit parts.
- 11-16: #478 (#424 review): -maxqueued 0 now means 1 frame queued (bench and test flag only).
- 11-16: #479 P-50 (#24): the rain test shot waits for a settled frame; suite 57 pass, 0 fail.
- 11-16: #480 #24 p2 r2: dungeon creatures appear near players at their posts and stay dead once killed (host; joiners in r3).
- 11-16: #481 W-174 r2 (#406, closes #403): one natural ground-edit profile for pass gullies, beds, pads and paddies; landmarks 114 9786243F (campsites moved, the small fungal grotto at -1796,-201 is gone).
- 11-16: #483 R-23 r3 (#298): one lighting path; cloud and wide shadows dim only direct sun, so sunlit colossus faces under cloud are about 25% darker even in Clear (shots PunchyMilestones/R-23/row3).
- 11-16: #484 P-37 r1 (#369): no loading screen; the camera holds on the sky until the world is in, then tilts down (`-loadveil` brings the old veil back).
- 11-16: #485 P-49 (#24, #27): dungeon quests; a notice board names a cave or ruin-room chest (heirloom Key, elder Staff) and you bring back what it holds.
- 11-16: #486 W-166 r4 (#336), #24 p2: colossi wear library wood, stone, bone and ice; cave and ruin chests draw from their own loot rows.
- 11-16: #488 A-134 (#460): households own their houses and goods; a theft seen by someone home or a guard brings a guard over, and that household refuses favours for 3 days.
- 11-16: #489 W-176 (#475): cave side passages no longer dig pits through other passages or step down into their end chamber; new layout test finds 0 pits, 0 ledges in 23 caves.
- 11-16: #490 P-37 r2 (#369): players no longer light the dark themselves; every new character starts with a torch, and the cave lantern glows only while a light item is held (seen by other players too).
- 11-16: #491 P-49 r3 (#27): dungeon and find quests now ask for their own quest-only items (Heirloom key, Elder staff, Lost key); random loot never hands these out, and old saves place them again.
- 11-16: #495 A-134 r4 (#460): after a seen theft, someone at home now steps out of the door within seconds to confront you, then goes back in.
- 11-16: #493 P-35 r2 (#26): every item now also has its own catalog asset, checked against the item table at start; nothing changes in play.
- 11-16: #496 P-49 (#27): the Elder staff quest item is held in the hand like the Staff.
- 11-16: #497 #24 p2 r3 (#24): a joined player now sees the same dungeon creatures dead as the host (late joiners too).
- 11-16: #498 A-134 r5 (#460): steal while you are inside a room and the guard waits, then walks to the door and says "Halt!" when you step out.
- 11-16: #500 render/hour-sync-doc, testing.md note: set the hour and take frozen shots on the host (from the #494 check).
- 11-16: #501 folk/innkeeper-test-order, innkeeper PlayMode tests remake town members after setting the hour (test-only).
- 11-16: #502 player/seamless-2, P-37 r3 (#369): walk-in storm houses with -seamless (host's choice sent on join; old and new builds can't join each other). Playtest: a shut door blocks, and the door works from inside.
- 11-16: #503 world/ground-edits, W-174 r3 (#406) + W-176 (#475): river banks, falls pools and pool lips on the shared ground-edit profile; cave side passages without pits. No re-pins.
- 11-16: #504 world/map-edits-bake, M-07 r2b (#260): painted map edits reach the game world; edits change the join signature only when present. One island cache miss on first load.
- 11-16: #505 fauna/dungeon-r5, #24 phase 2 r4-r5: dungeon props drawn by screen size; dungeon creatures roost, patrol and sleep at socket posts (bats on ceilings). Playtest: bats roosting in caves.
- 11-16: #506 fauna/walkin-walls-doors, P-37 r3 row 2 (#369): creatures stay out of walk-in houses unless the door is open.
- 11-16: #507 world/cave-walls, W-172 r7 (#262): cave wall details by biome, rock and cave type. Playtest: fungi caps may look too dark; volcanic caves have none yet.
- 11-16: #508 world/parts-units-4, W-175 r4-6 (#415): kit part format, building parts, nested units; Lab Snap joins button. No look change.
- 11-16: #509 world/chunk-mesh-budget (#400): tunnel-entry hitch; chunk-mesh batches leave half the workers free, cave-mouth chunks count 5x. Playtest: walk into a tunnel and watch for a stall.
- 11-16: #510 player/item-ids, P-35 r3 rows 2-5 (#26): item record ids, stacks, catalog in the world sync check. Playtest: take from a walk-in chest solo and as a joiner.
- 11-16: #512 world/ground-holes, W-174 r4a (#405): one hole mask for cave mouths, temple entrances and scarp strips. Playtest: no plants standing in the sunken temple's opening or on scarp steps.
- 2026-11-16: #513 R-40 (#511) soft wide shadows, one shadow per point at the URP hand-off (render).
- 2026-11-16: #514 P-51 r2 (#337) walk-in elf towers with a climbable stair under -seamless (player-2). Playtest: enter an elf tower, climb to the top floor.
- 2026-11-16: #515 W-174 r4b-5 (#405) one seam builder: no gaps round cave mouths, temple entrances and scarps (world). Playtest: walk the sunken temple's entrance and a cave mouth edge.
- 2026-11-16: #516 W-175 (#415) a Cookhouse built from a JSON blueprint stands in human villages (world). Playtest: find it near 2957,-834.
- 2026-11-16: #517 P-51 r3 (#337) elf tower roof hatch, roof deck and windows under -seamless (player-2). Playtest: climb to the deck.
- 2026-11-16: #519 P-52 (#518) long frames move the player in 0.05 s pieces, no more walking out through tower walls on a hitch (player-2). Playtest: climb a tower stair while the game hitches.
- 2026-11-16: #520 W-174 r6 row 1 (#405) the sunken temple entrance meets the ground with no gaps or lips at any distance (world). Playtest: walk the temple rim and view it from 150 m.
- 2026-11-16: #521 W-172 (#365) cave fungi caps are paler with a soft warm glow, no longer black deep in caves (world). Playtest: go more than 8 m into a cave.
- 2026-11-16: #524 P-52 tail (#518) a joiner no longer slips out through tower 75's wall on a slow frame on the stair (player). Playtest: join a host, climb a walk-in tower while the game hitches.
- 2026-11-16: #525 A-135 (#405, #406) creatures keep off removed ground: no goblins in the temple entrance cut, none walk over scarp holes (fauna). Playtest: visit the sunken temple entrance.
- 2026-11-16: #526 W-174 r6 row 2 (#405) cave mouths: the ground meets the cave walls with no gap or lip, even on steep slopes (world). Playtest: walk into a cave on a hillside and look back at the rim.
- 2026-11-16: #527 W-177 (#522) cave fungi patches grow real mushroom clusters, glowing caps on GlowFungi, caps turned up on walls (world). Playtest: find a fungal cave or glowing wall patch.
- #530 W-180 (#529): cave mouths showing sky was a shot artifact (cull lags a camera move by one frame); docs gotcha only, no game change.
- #531 W-178 r1 (#523): glowing cave tendrils over glow fungi, sway and part on touch; shared local ChainSim.
- #532 W-179 r1 (#528): one shared cave haze; fungi, tendrils, bats and creatures now sit in the cave's air.
- #533 W-178 r2 (#523): cave set pieces (root curtains, vine drapes, mine ropes) pushed by player/creatures; tendril glow tinted per cave style.
- #535 W-162 (#327): one shared rock wall for cliffs and caves; climb grades (craggy 4, smooth 5); cliffs over 60 deg stop non-climbers; cave mouth toes.
- #536 W-153 (#301): water/truth landed: paddies bake in IslandV2, steep cells rejected at bake, water truth counts in WorldTests.
- #537 W-153 (#301): uphillGraph probe skips lake edges, 62 -> 10; probe only, no change in play.
- #538 W-153 row 3 (#301): rivers cut steep notches through rises left by later passes (scarp-kit banks); channels above falls run to the lip as small falls.
- #539 W-153 (#301): small-fall channel water from #538 reverted (it made standing water mesas); notches kept; river rework design doc.
- 11-17 #541 W-153 (#301): river rework rows 1-3 (segment profiles, join blending, falls with foot pools; 138 falls). Landmarks_V3 re-pinned 114 9113464F.
- 11-17 #542 W-166 row 1 (#336): cave walls take their relief from the general stone table (granite, sandstone, tuff tuned; detail fades by screen size).
- 11-17 #543 W-153 row 3b (#301): waterfalls get a curtain mesh of the water itself (arc out and down, ragged edges), detail by screen size.
- 11-17 #540 R-29 (#320): one shared electricity effect; spire and golem arcs re-jag erratically with a soft glow.
- 11-17 #544 W-166 wood (#336): each wood kind has its own board size, knot spacing, grain figure and wear (pine, driftwood, dark, bleached tuned).
- 11-17 #545 W-166 (#336): MaterialLibrary tests follow includes; Cookhouse roofs plaster, door mats cloth, camp fire rings now stone.
- 11-17 #546 W-166 (#336): creature coverings (fur, feathers, scales, skin, chitin) in the material library; no look change yet.
- 11-17 #547 (#529): no see-through cave mouth on the frame after a camera cut or teleport.
- 11-17 #548 W-166 (#336): creatures draw coats through the material library; no look change.
