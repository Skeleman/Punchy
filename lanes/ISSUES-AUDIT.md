# Open issues audit: resolved vs open (2026-11-02)

Read-only audit of all 54 open issues against REPORTS-world.md, REPORTS-animal.md, lanes/*/STATE.md, batches/*.md and git.
Heads: origin/main 41a9259, origin/world-features f137084, origin/animal-behaviours e3b8521.
"In main?" = `git merge-base --is-ancestor <commit> origin/main`. Lane-only today: world W-30..W-42 (0bdc707..f137084), animal A-35..A-41 (4c03cde..e3b8521).

Verdicts: RESOLVED = the asked-for work is done (often still waiting for Zach's look before closing). PARTLY = some of it done. OPEN = nothing done. OPEN-roadmap = long-term idea, not built.

Counts: RESOLVED 23, PARTLY 8, OPEN 14, OPEN-roadmap 8, on hold 1.

| Issue | Title | Verdict | Evidence | In main? | What's left |
|---|---|---|---|---|---|
| #3 | Z-34: cave streams end in a pool or drop | RESOLVED | W-06 report: v1 pool 5, v2 pool 12 hole 2, left out 0; z34-pool 22e7534 | yes | Rule done; W-47 row 2c is a new Zach report (stream end at -3813,767 looks unconvincing), a look fix rather than the rule |
| #7 | Z-51: several voices per animal | RESOLVED | A-20 Growl in build (e2eda9a); A-23 side-on dog chase (5557c9a); Yowl/Whine/Bark/DogHowl/Hiss heard A-02/A-15 | yes | Zach's OK |
| #8 | Z-52: NPC reactions to the player's race | PARTLY | Stances merged with P3; A-01 odds match the table; A-03 only 2 Dwarf Rival lines seen live | yes | Live check of each race pair (scripted hook) |
| #9 | Z-53: predator-prey chases; prey eaten | RESOLVED | A-01: 28 pounces, 9 landed, 7 ate; A-09 -shotonhunt shot (767d3ca) | yes | Zach's OK |
| #10 | New species: fish folk, blind cave pygmies | OPEN-roadmap | Stances stored as data only; species not built | - | Whole feature |
| #11 | Z-21: long-stride foot bends too steeply | RESOLVED | A-05 ankle capped 38 deg (609c5e5); A-41 run pose e3b8521 on top | 609c5e5 yes; A-41 lane only | Merge A-41 (M-04); Zach's OK |
| #12 | Z-07: songbirds and dormice spin while wandering | PARTLY | A-06b flare fix 30fae95, A-12/A-13 turn damp; A-16 Wander ~22.5 deg/s (target <20) | yes | Zach judges in a playtest (issue comment) |
| #13 | Z-35: waterfall fall speed | RESOLVED | b39522b + W-22 row 4 fall speed 2x; ISSUES-MAP: Zach OKed | yes | Close (map says after M-04) |
| #14 | Cave mouths: grey shards not reproduced | PARTLY | W-04 f4c7239 "no trace marks over cave holes" (guessed cause, unverified); W-04b shot missed the mouth | yes | Reproduce or get a spot from Zach; W-46 row 2c queued |
| #15 | World look checks still owed | RESOLVED | W-11 hall timber close-up (959fd34); W-13 MarshFog over two non-marsh rivers (fd25279) | yes | Both checklist items done; Zach's OK. W-54 row redundant |
| #16 | Z-20: carrying a large animal | RESOLVED | A-19 carry by weight (e37a463); A-21 child carried 2/2 in build (08da339) | yes | Zach's OK |
| #17 | Z-54: ocotillo mimics not found | RESOLVED | W-06: spawn rule and density listed (spawns on v1); A-11/A-12 arm-leg mimic polish (36f6dab) | yes | Optional: rest look closer to a real ocotillo |
| #18 | Z-60: natural river curves and width changes | RESOLVED | W-05b Hermite curves, width 9-45 m (e3ab4af); W-07 380d59a | yes | W-53 meander-by-width polish queued (not needed for the issue) |
| #19 | Z-61: poison swamp small joined pools | RESOLVED | W-09/W-10 pond mask, W-11 v2 ponds (959fd34); W-25 irregular paddy pools (a19bc79) | yes | Zach's look |
| #20 | Z-62: mountain ranges dividing biome regions | PARTLY | W-08/W-09 v2 ridge chains (149bc0a, 0e19871); v2 realms | yes | Roads winding over or tunnelling through ranges |
| #21 | Z-63: plant fidelity experiments | OPEN | No report | - | Whole task (needs design) |
| #22 | Z-64: underwater camera, visuals, sound | OPEN | No report | - | Whole task (needs design) |
| #23 | Realms: large regions grouping biomes | RESOLVED | W-05 layout B five realms (b5c7f88); W-22 v2 default; 96a80de; profiler recheck on 41a9259 passes; A-24 "Zach can close #23" | yes | Close after Zach's OK |
| #24 | Dungeons | OPEN-roadmap | ROADMAP-IDEAS only | - | Whole feature |
| #25 | NPCs and characters | OPEN-roadmap | ROADMAP-IDEAS only | - | Whole feature |
| #26 | Items and inventory | OPEN-roadmap | ROADMAP-IDEAS only | - | Whole feature |
| #27 | Quests | OPEN-roadmap | ROADMAP-IDEAS only | - | Whole feature |
| #28 | Linux build | OPEN (on hold) | Zach: on hold, Proton works | - | Nothing until Zach unholds |
| #29 | Supervisor handover record | RESOLVED | Handover done: v0.11.0 published (#2 closed), profiler brief 60461a1, lanes running on main account | - | Tick the boxes and close |
| #30 | Flowers swivel and stretch | RESOLVED | W-02/W-02b: wind-shear shader cause, bend keeps length (ec53f8a, 3728ef9); W-07 storm shot; W-08 pair 149bc0a | yes | Zach's look; the controlled side-view check never ran cleanly. W-54 row may be redundant |
| #31 | Laptop: houses lit wrong | OPEN | Profiler diag only (574167c): not tier/GPU/quality; stone walls dark | - | Same-view shot on both machines, then a fix |
| #32 | Roof edges bevelled | RESOLVED | W-04 bevelled eaves f4c7239 + Z-41 ridge bfd69e5; W-43 card quotes Zach "Beveled edges on roofs looks much better" | yes | W-45 row 2b only for new kit roofs |
| #34 | Blue moon much larger | RESOLVED | Radius 0.034 -> 0.22 rad (a96a1dc, 959fd34); face/glow W-12/W-13 (482e7c8, fd25279) | yes | Zach's OK; W-51 row 2b repeats done work unless Zach wants it bigger |
| #35 | Bat folk: cave race, settlements, white pools | PARTLY | W-15..W-20: BatFolkBaking, white pools, hanging huts and props | yes | Residents (animal lane), goggles, face covering, no-lights rule |
| #36 | Coastal settlements with boats | OPEN-roadmap | None (W-30 archipelago is islands, not settlements) | - | Whole feature |
| #37 | Sea folk underwater settlement | OPEN-roadmap | None | - | Whole feature |
| #38 | Turtle folk mountaintop igloos | OPEN-roadmap | None | - | Whole feature |
| #39 | Roads: cobblestone, paved settlements, tunnels | PARTLY | W-09 paving channel (0e19871), W-10 cobbles (c5ddbe2), W-34 rounded cobbles (0bdc707) | 0e19871/c5ddbe2 yes; 0bdc707 lane only | Road tunnels; merge W-34 |
| #40 | Colossi: one giant per biome | PARTLY | A-09/A-12 Dunes colossus (36f6dab); Ocean serpent existed | yes | Colossi for the other biomes |
| #41 | Carnivorous plant creature | RESOLVED | A-10..A-10c Mantrap: hit check 96816e5, head normals fix ae5b491 | yes | Zach's OK |
| #42 | Waterfalls along rivers | RESOLVED | W-05b falls on 4 m drops (e3ab4af); W-06 filter 646202c; W-07 380d59a; W-29 grand falls d9ce83c | yes | Zach's OK |
| #43 | Ambient animal sounds fewer, per biome | RESOLVED | A-03 per-biome sets, silent biomes; A-18 36.5 calls/min (b549d00) | yes | Zach's OK |
| #45 | Shooting stars rarer and thinner | RESOLVED | W-03 half as thick, fade; W-04 10x rarer (f4c7239); W-42 another 20x rarer + red-orange (f137084) | W-04 yes; W-42 lane only | W-42 shots failed (reshoot); merge W-42 |
| #46 | Animals flee flying player: count height | PARTLY | A-03 3D fear; A-07 height gap x2.5 past 1.5 m (32f90e4); A-13 flat-spot test shows no gain or harm | yes | Zach judges in play; no measured improvement yet |
| #47 | Fish sprint keeps depth, faster | RESOLVED | A-04 sprint 2.4-2.8x, holds depth (b5e9715) | yes | Zach's OK |
| #48 | Right face button: dodge/roll, descend | RESOLVED | A-04 roll/descend/sink (b5e9715); A-16 remote roll seen on client (995ca68) | yes | Zach's look (issue comment) |
| #49 | Client NRE in TerrainSource.HasBiomes on host quit | RESOLVED | A-17 re-fetch TerrainSource after world swap (17d4b40); A-18 0 exceptions, 45 s solo | yes | Close; W-54 row redundant |
| #50 | Personality values (aggression first) | RESOLVED | A-19 CreaturePersonality aggression, host/client identical (e37a463) | yes | Further traits are new work |
| #54 | Camera cave arm trace in managed code | OPEN | From ECS review of 41a9259; A-45 card not written yet | - | Burst the trace |
| #55 | TraceSystem samples ground before footfall check | OPEN | ECS review; no card run | - | Reorder / snapshot |
| #56 | Shared per-frame creature snapshot | OPEN | ECS review; no card run | - | Whole task |
| #57 | Stagger predator/alarm/chat searches | OPEN | ECS review; no card run | - | Whole task |
| #58 | Split CreatureState by reader | OPEN | ECS review (needs design) | - | Whole task |
| #59 | Brain LOD for far creatures | OPEN | ECS review | - | Whole task |
| #60 | Decide as an activity table | OPEN | ECS review (needs design) | - | Whole task |
| #61 | Split CreatureBrainSystem.cs into partials | OPEN | ECS review | - | Whole task |
| #62 | MonoBehaviours driving ECS: order and deps | OPEN | ECS review (needs design) | - | Whole task |
| #63 | Managed TerrainSource sampling: audit call sites | OPEN | ECS review; A-22 Burst cave cull predates it (already in 41a9259) | - | Audit the listed call sites |
| #64 | Creature needs invisible to the player | OPEN | ECS review (needs design) | - | Design call |

RESOLVED (23): #3 #7 #9 #11 #13 #15 #16 #17 #18 #19 #23 #29 #30 #32 #34 #41 #42 #43 #45 #47 #48 #49 #50.
All 23 have their main fix in origin/main. Follow-ups still lane-only: #11 (A-41), #45 (W-42), plus #39 (W-34) among the PARTLY ones.
Queued rows that repeat done work: W-51 2b (#34), W-54 (#15, #30, #49).
