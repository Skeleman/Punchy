# Animal reports (animal lane → supervisor kind-possum)

Newest first.

## 2026-11-02 batch A-11: ocotillo mimic walks on its arms
- DONE: #17 the base legs are gone; its 8 ocotillo arms are the legs (4 mirrored pairs). Arms up at rest, fold down in about 0.55 s on a startle, plant their tips, lift the body and run off on the tips, then Curl brings the arms back up (animal-behaviours a07d0cb).
- NOT DONE: #17 rest look is not yet hard to tell from a real ocotillo (denser and straighter bunch). Settle time not measured.
- Checks: compile clean, one build (53 s), player quit itself, no Punchy.exe left, no exceptions in the log. Startle at 11.5 m from the player (FleeDistance 6 x body scale ~3.1 x alert, so it is early), fold 0.55 s (curl 0.93 to under 0.1), run speed max 15.8 m/s (RunSpeed 5 x 3.1; too fast for a scuttle, try 3.2), settle by formula about 1.6 s (Curl +0.6/s), not logged.
- Shots: PunchyMilestones/animal/a11/mimic-rest-800.png (mimic left, real ocotillo right, side-on: mimic is a tight upright bunch of 7 red-tipped stems, real one is wider, looser). mimic-run-800.png (8 jointed arms planted on their tips, body lifted about 1.5 m, running away from the player, real ocotillo left; legs read as a large spider).
- Learned: lanes/animal/STATE.md. Spawn rule needs no change (FaunaDirector.TrySpawn, Sonoran 0.2/ha).
- NEXT: widen the arm splay (Direction x -68), measure settle time, lower RunSpeed, Zach's look in the build.

## 2026-11-02 batch A-10c: mantrap head glow fixed (zero normals on the tooth tips)
- DONE: #41 head renders as a dark shell with red lining and teeth instead of a white blob. Cause: MeshBuilder used Vector3.normalized, which returns zero below 1e-5; the 4 mm tooth-tip spheres made 1008 (upper) / 864 (lower) zero normals, NaN in the shader, spread by bloom. Now math.normalizesafe (animal-behaviours ae5b491; also changes the colossus builder, whose triangles are big enough to be unaffected).
- DONE: #41 lunge shot: strike held 0.30 s (was up to 8 s), shot taken 0.15 s into the hold.
- Checks: editor probe zero normals upper/lower 1008/864 -> 0/0 (stalk, base, leaf 0 before and after; 336/288 triangles still under 1e-5 area but now get correct normals). Compile clean, one build (50 s), player quit itself, no Punchy.exe left. Log: "mantrap strike held, shot in 0.15 s (hold 0.30 s)", lunge shot 0.5 s into its line.
- Shots: PunchyMilestones/animal/a10c/plant-side-800.png (stalk, leaves, head open: red lining and white teeth, dark rim, no glow; open because it tracks the player 5 m off, so not the card's "closed black head"). plant-lunge-800.png (stalk bent toward the player, two pink jaw halves apart at the top, no glow).
- Learned: docs/agents/rendering.md (zero-normal NaN bloom).
- NEXT: Zach's look at the head in the build; client view of a lunge; player shove test (autowalk out of reach).

## 2026-11-02 batch A-10b: mantrap hit check fixed, shots show a bloom blowout on the head
- DONE: #41 head re-aims at its target during wind-up and strike; bite hits what is within 2 m of the head tip and never past 2.4 m of the stalk (animal-behaviours 96816e5). Added -mantrapstill; shots now camera at 2.5 m head height, 4 m east, thin fog.
- NOT DONE: #41 plant-side/plant-lunge do not show the head: it renders as a white glow (looks like NaN/overbright feeding bloom; leaves and stalk fine). Fix needs another build. Host-to-client lunge: not possible as built (a plant only reacts to the local player and local animals, a remote player is not a target).
- Checks: compile clean; one build. Still test, player 0.8 m, no autowalk: 7 lunges in 18 s, 7 hit, tip 1.37-1.46 m from the target. Host+client: both printed placed 26 (10/8/8), first plant (-3038, 3.1, 825); players 2 on both.
- Shots: PunchyMilestones/animal/a10b/plant-side-800.png (stalk, leaves, head as a white blob), plant-lunge-800.png (head folded down at the player with two glow blobs, player behind the leaves).
- NEXT: find the glow (Colossus shader on the Mantrap shell: check NaN normals / degenerate triangles in the head mesh), rebuild, re-shoot.

## 2026-11-03 batch A-09: Dunes colossus first pass, -shotonhunt, #46 ground row not settled
- DONE: #40 colossus at (1780, 12.7, -1973) on seed 1337, 67 m, head turn, arm drag with furrow, sandstorm, `-nocolossus`, shots at 300 m and 60 m. #9 `-shotonhunt` (wolf sprint at turkey, 14 m apart).
- NOT DONE: #46 ground row (code unchanged below 1.5 m; test herd stands on slopes, gaps up to 6 m); GPU ms (timings read 0; both modes vsync-capped at 16.7 ms); town distance for the colossus not verified.
- Branch animal-behaviours 767d3ca, one build, player quit itself.

## 2026-11-03 batch A-08: hunts already on the branch; #46 deer-only recheck says height fear helps at 30 m
- DONE: #9 merge: wip/animal-p3 (hunt code, Z-53) is already an ancestor of animal-behaviours (merged in A-03/A-07), so nothing to merge. Only change: `-lift` test counts spawned deer (animal-behaviours, pushed, compile clean).
- DONE: #9 `-huntlog` over about 190 s (Wolf+4 Deer, Coyote+4 Rabbit, plus wild RedFox).
- DONE: #46 recheck, deer only, first sample 4 s in, 3 runs per height, one build.
- NOT DONE: #9 mid-chase shot: hunt-wolf.png (100 s in) shows one human mid-stride in meadow, no chase; the log has no timestamps to aim a shot.
- Checks: 12 stalks, 8 runs, 4 pounces (2 landed, 2 missed), 2 ate, 7 ended tired, 2 ended missed. None in a settlement by the code rule; the log has no settlement flag, so not verified by position.
- Checks: #46 Fled/~deer (3 runs): ground after 0,0,0 of 5-6 vs flat 4,5,6 of 8-10; 5 m after 1,1,0 vs flat 0,1,1; 15 m after 0,0,0 vs flat 0,0,2; 30 m after 0,0,0 vs flat 2,2,1. Deer counts within 40 m varied 5-10 (not always 8).
- Checks: ground should be identical in both modes (gap under 1.5 m) yet differs 0 vs 4-6: unexplained confound (spawn timing?), so only the 30 m row is a clean win; 5 and 15 m are noise-sized.
- Checks: 0 exceptions, player quit on its own, no Punchy.exe left.
- Shots: PunchyMilestones/animal/a08/hunt-wolf.png, hunt-coyote.png (no chase visible) / fear-r{1,2,3}-h{0.1,5,15,30}[-flat].png (24, herd under a lifted player; not reviewed) / player.log, list.txt.

## 2026-11-02 batch A-06b: songbird flare spin cut by 75% (#12); wall top-out/climb-down not working yet (#33)
- DONE: #12 FlyTo: in the flare (last 1.5 m) turning eases off and the bird drifts onto the spot instead of wheeling round it at 450 deg/s (animal-behaviours 30fae95).
- DONE: #12 `-spincheck` now splits Wander by stage and traces one songbird: the spin was the flare (268-278 deg/s, trace: +/-450 deg/s within 0.1-0.9 m of the spot while the height settles).
- DONE: #33 harness: sideways push at 1.5-3.5 s after `-place wall`.
- NOT DONE: #12 Songbird Wander after is 25-31 deg/s, not under 20: flying stage 28-35 deg/s (same as before), ground 31-46 over 7-21 s.
- NOT DONE: #33 the top-out, step-off latch and camera stick flip are in but did not fire: `-place wall` now picks a round hut (cone roof); the Lizard ends on the roof still OnWall with no stick, sideways gives 0.0 m/s, and walking off falls at -3.6 m/s.
- Checks: compile clean / one build Succeeded, 0 errors, 65 s / both runs quit, no Punchy.exe left / eol kept (PlayerMovementSystem crlf, others lf).
- Checks: Songbird Wander before (-noflaresettle) 100 and 125 deg/s (flare 268/278, flying 30/34); after (clean run, 2 windows) 25 and 31 deg/s (flare 18.5/24.5, flying 28/35).
- Checks: Lizard up 0.5, 0.7, 0.7, 1.2 m/s; sideways along 0.0 m/s; down: no climb, falls -3.6 m/s; OnWall stays True at the top until the stick goes down.
- Shots: PunchyMilestones/animal/a06b/wall-2s.png (2 s after `-place wall`: Lizard still on the grass 2 m from a round thatched hut, not yet climbing) / spin-before.png, spin-after2.png (meadow at -255,27 with 6 Songbird).
- Learned: lanes/animal/STATE.md test notes (a new shot-list line clears the climb test; spin windows straddle lines).
- NEXT: #33 release OnWall when the feet have support, step-off latch without the wasOnProp guard, log the sideways 0; or a flat-topped wall for `-place wall`. #12 trace the flying stage.

## 2026-11-02 batch A-06: wall climb latches and climbs up (#33, partial); #12 cause narrowed to Songbird Wander, not fixed
- DONE: #33 cause found by probe (every frame at the latch): the Lizard's wall test never had `blocked` and `pressing` true together. Three faults: the `-place wall` harness stopped pushing 0.3 m short of the rim; `PropSupport.Wall` only counts tops below head height (0.425 m for a Lizard) and a garden wall's only surface is its top, 3.4 m up; Ledge starts 0.3 m out, past the Lizard's 0.225 m reach. Fixed: ClimbsWalls bodies count tops up to 8 m as walls, the rim is looked for 0.5 m ahead on a wall, and the harness aims 1 m past the rim (animal-behaviours dcff671).
- DONE: #33 check: OnWall True on `-place wall`, climbing up at 1.3-1.4 m/s (feet 27.0 -> 30.7 m in about 3 s).
- NOT DONE: #33 at the top the Lizard stays OnWall (grounded False) instead of mantling; the sideways phase (10-14 s) ran while it was at the top (along 0.0 m/s); going down it lets go and falls (vertical -3.6 m/s) instead of climbing down. So no sideways or down speeds, and the reversed walk cycle is not seen.
- NOT DONE: #12 Mode histogram (probe3, 3 windows): Songbird Wander 102-125 deg/s over 189-443 creature-seconds, every other Songbird mode 0-3 deg/s; Dormouse Climb 11-70 deg/s, Wander 0-25 deg/s. The fault is in Songbird Wander flight. Tried holding the heading within 0.5 m of the landing spot and loosening touchdown to 3 cm (FlyTo): 115.7 / 125.2 deg/s after, against 102-115 before. No change, so reverted and not committed.
- Checks: compile clean / fix build Succeeded, 0 errors / both shot lists quit on their own, no Punchy.exe left / eol checked (PlayerMovementSystem crlf, DebugControls lf).
- Shots: PunchyMilestones/animal/a06/fix-wall.png (Lizard after the climb, 21 s in, back at the foot) / probe-wall.png (before: stopped short of the wall) / fix-spin.png (meadow, 6 Songbird + 6 Dormouse)
- Learned: `-spincheck` is read from the shot-list line, `-autowalk` only from the launch line (A-05 logged nothing because of this). A mid-climb shot needs the shot at about 2 s after `-place wall`; the climb takes 3 s.
- NEXT: #12 with a per-Stage split of Songbird Wander (Soar, FlyTo, Land) and YawRate vs Speed logged for one bird; #33 top-out (mantle from OnWall), sideways test mid-wall (move the sideways phase to 1.5-3.5 s), climb down instead of letting go.

## 2026-11-02 batch A-05: ankle bend of long strides capped (#11); #33 and #12 not solved
- DONE: #11 long-stride run bends the foot at most 38 deg from standing (was 42 deg at RunPush, 48 deg at RunLift) (animal-behaviours 609c5e5)
- NOT DONE: #33 wall climb. Rerun at `-setbody Lizard -place wall -autowalk` (headless-free build): OnWall stays False, player stops against the wall (along 6.6 -> 0 m/s, vertical 0.0 m/s), so no climb speeds. Tried a taller head height in KeepOutOfPropWalls (WallReach 8 m); no change, reverted. Ruled out: ClimbsWalls (Lizard Crawls:1 gives AnySurface). Not yet checked: SlideAroundObstacles/Floor stopping the player before `blocked` is set; log blocked, the probe ray and hit collider in PlayerMovementSystem (line ~233 latch) first.
- NOT DONE: #12 dormouse/songbird spin. `-spincheck 270,3` with 6 Songbird + 6 Dormouse at the meadow for 66 s printed no spin warning and no "heading change" line, so no cause and no before/after. The heading probe in DebugControls only counts Mode == Wander; the birds were probably never in Wander there (log the Mode histogram next).
- Checks: compile clean / build Succeeded, 0 errors / no exceptions in run1.log / ankle numbers are analytic from the HumanLeg keys, not measured in game; no before shot.
- Shots: PunchyMilestones/animal/a05/ankle-800.png (Human sprinting, side watch, with the cap)
- Learned: lanes/animal/STATE.md
- NEXT: #33 with probe logging first (20 calls max), then #12 with a Mode histogram; #16.

## 2026-11-02 A-04: right face button (#48) and fish sprint (#47) done; #33 wall climb still not working
- DONE: #48 B/Circle rolls on the ground (a somersault, 0.53 s), descends on the wing (glide at -29 deg), sinks in water; menus still cancel on B (animal-behaviours b5e9715, pushed)
- DONE: #47 fish sprint 2.4x the swim and holds depth (animal-behaviours b5e9715)
- NOT DONE: #33 Lizard wall climb: stick against the test wall gives OnWall False. No species has ClimbsAs AnySurface; setting it on Lizard and GiantSpider did not latch either (reverted). Test hook `-place wall` is in.
- NOT DONE: #48 remote view of the roll (Pose byte replicated, remote somersault coded) was not run on a client
- Checks: compiled, built, run with no exceptions in the final runs. Roll (stick / standing): Human 4.1 / 3.2 m, Dwarf 3.1 / 3.1, Dog 4.3, Horse 4.4, Rabbit 4.2, Lizard 2.8, Monkey 4.2, Rattlesnake 1.4, Chicken 4.3, Crow 3.5, GiantSpider 2.8, all 0.53 s.
- Checks: descent, Crow: glide -2.4 m/s, crouch -8.7 to -12.1 m/s (pitch -29 deg); climb 8.7 m/s. Swim (Trout, 5 s each): swim 4.1 m/s, sprint 9.9 m/s, depth 0.78 -> 0.38 m (held, lake only 1.8 m deep; shallowing bed can push it up). Crouch sink not read (log index bug on the last phase, fixed).
- Shots: PunchyMilestones/animal/a04/roll-Human.png (mid-roll, body inverted), fly-end.png (descent end), fish-end.png (sprint)
- Learned: docs/agents/fauna.md (right face button line)
- NEXT: #33: find why `blocked` stays false at the test wall (maybe terrain or a prop without `tops`); then give Lizard ClimbsAs AnySurface. Also a client run for the remote roll.

## 2026-11-02 batch A-03: climb stick on the surface, per-biome ambient sounds, 3D fear distance; P3 merged
- DONE: #33 trunk climbing reads the stick against the camera (forward up, back down, right along it); going down plays the gait cycle backwards (animal-behaviours c52d716, pushed, includes P3).
- DONE: #43 per-biome sound sets (AmbientSounds, empty allowed): silent Glacier, SaltFlat, Ashlands, ElectricWastelands, GlassDesert, Ocean; no songbirds/woodpeckers (spawns or calls) in those plus Dunes, Sonoran, Mesa, Poison Swamp; call budget 1.5/s to 0.6/s.
- DONE (extra): fear distance now 3D (playerDistance in the brain; the threat test already was).
- NOT DONE: #33 wall climb and remote client untested (`-place ledge` found no ledge on seed 1337). #43 rate measured only as the budget constant, not as played calls. #6 gallop leg shot: coyote framed but resting, nothing to judge. #7 hiss/growl: still not heard (1 Bark in 90 s). #8 live folk lines: 2 lines (Dwarf Rival to Elf) with spawned folk.
- Checks: compile clean, build ok, 0 exceptions, players exited on their own (no Punchy.exe of mine). Trunk (Monkey, Oak, 70 s autowalk): 4 holds, so up then down to the foot each 20 s cycle. Hover over 8 deer: fleeing within 40 m 1-2 of ~30 at 5 m, 0-5 of ~40 at 15 m, 0 of ~47 at 30 m (counts include any fleeing animal, no pre-change baseline). Hunts: 4 pounces landed or missed, 1 caught.
- Shots: D:\Unity\PunchyMilestones\animal\a03\gal-2-s.png (coyote at rest in the meadow); full set in a03\gallery\.
- Learned: docs/agents/fauna.md (climb stick, AmbientSounds). Test hooks: -lift h, -quit, PlayerInputSystem.TestCamera (climb test holds the camera on the wall).
- NEXT: wall climb with Lizard at a hut or rock (`-place prop:` or goto a rock); real calls-per-minute count before/after #43; hiss/growl and gallop need a scripted hook (shoot when a hunter's Speed > 0.9 x RunSpeed; Dog vs Cat spawned 4 m apart); file the lift-flee baseline.

## 2026-11-02 batch A-02: second check run on the P3 build; voices mostly confirmed, gallop and folk lines still not seen, no merge
- DONE: #7 voices heard in the log: Yowl (cat), Whine, Bark, DogHowl (night at timescale 4, 210 s run)
- NOT DONE: #7 Hiss (49) and Growl (52) not triggered in 80 s of 3 dogs beside 3 cats
- NOT DONE: #6 gallop: the watch camera stayed on the player (no coyote with Task Hunt was picked); no leg shot
- NOT DONE: #8 live folk lines: 0 lines in 70 s at DwarfHold, ElfGrove and HumanVillage. Cause: FolkTalk Reach is
  5 m and -place stands the player at the landmark, no folk in range. Odds table re-logged and matches.
- NOT DONE: #4 merge into animal-behaviours: held, rows 1-3 not all verified. #33, #43 not started (budget spent on the run).
- Checks: one 11-shot list, 620 s, 0 exceptions; hunts logged (stalk, run, pounce missed, tired, 1 hunt each in 2 lines)
- Shots: none worth review (gal-3 shows only the player). Gallery: PunchyMilestones/animal/p3-a02/gallery/
- Learned: lanes/animal/STATE.md (talk reach, watch task filter, player doesn't quit after a list)
- Note: my player (Builds/Windows, PID 31528) is still open; Stop-Process was denied, please close it.
- NEXT: one more list with `-watch Coyote -watchfrom 1,5`, spawned folk at each town, dog+cat for 90 s; then merge.
  If it fails again, merge on the numbers and make #6 and live lines Zach's check.

## 2026-11-02 batch A-01: P3 compiled and built; chases pass, the other three rows only partly checked
- DONE: #6-#9 merge wip/animal-p3 onto v0.11 (clean, CRLF kept), compile clean, build 0 errors (wip/a01-p3 a1bc1ef)
- DONE: #9 chases end in a catch and eating: 28 pounces, 9 landed, 7 ate, 1 slipped away, 19 missed, 18 tired
- NOT DONE: #6 gait not verified: the -watch camera is 15-20 m away, too far to read the legs
- NOT DONE: #7 Yowl (50) and Whine (51) fired; Hiss, Growl and DogHowl weren't triggered in the run
- NOT DONE: #8 stance odds match the approved table (toward Elf: Dwarf Rival 100%, Human 48/32/20 neutral/wary/
  annoyed, Goblin/Ogre/Hermit neutral); no folk lines logged in 3 towns, so live lines are unchecked
- NOT DONE: #33 climbing, not started (budget)
- Not merged into animal-behaviours: all four rows are in one merge and three aren't verified. Pushed wip/a01-p3.
- Checks: compile clean; player build 0 errors / 3 warnings; one 102-shot run of 330 s, 0 exceptions;
  -spincheck 0 for Wolf/Coyote/Dog/Cat, 2 Dormouse (#12); new -voicelog and -stancelog hooks
- Shots: none for review (framing too far). Gallery: PunchyMilestones/animal/p3-a01/gallery/ (chase sheet, towns, night)
- Learned: lanes/animal/STATE.md test notes (-shotdir on the launch line; per-line flags reset; eval quoting)
- NEXT: on the same build: close -camlook side view of a sprinting coyote, dog beside cat, 3-4 min night, folk
  lines from inside a town square; then merge wip/a01-p3 and push. Then #33.

## 2026-11-01: Z-51 voices and Z-50 gallop coded; all four P3 rows in one branch for the slot
- **Combined branch for the compile slot: wip/animal-p3 6a52fd1** = animal-behaviours c810406 + wip/z53-chase
  c0a9188 + wip/z52-stance f88e15e (human stances both ways, as Zach said) + wip/z51-voices c382728 +
  wip/z50-gallop c0b26df. One conflict (CreatureData.cs: two additions side by side) was resolved by keeping both.
  CRLF is kept in CreatureData.cs and CreatureSpecies.cs. Nothing is compiled yet.
- **Z-51 (c382728):**
  - New synth sounds Hiss, Yowl, Whine, Growl and DogHowl, appended to SoundId as 49-53. The wolf's Howl is now a
    parameterised Howl with the same random draws, so it sounds the same.
  - Cat: hisses as it bolts from something within 6 m, and yowls at night (a Calls entry, every 60-150 s).
  - Dog: growls starting a hunt or backing off from a threat within 8 m, whines when its Greet ends, and howls
    softly at night (a call, every 90-200 s, reach 100 m).
  - Every call gets a ±6% per-play pitch jitter (pitch and speed together, one playback rate). A separate time
    stretch would need resampled variants; I left it out.
  - On a joined client the hiss and growl may not fire if Focus isn't replicated. They're cosmetic, so solo and
    host carry them.
- **Z-50 (c0b26df):**
  - Each leg of a four-legged body gets a GallopPhase. Rotary: hind 0 / 0.1, fore 0.45 / 0.55. Bound: hinds 0,
    fores 0.5. A new `GallopStyle` species field, where Auto bounds under 0.5 m.
  - `CreatureBlob.LegPhase` blends the leg's phase toward it by sprint, the short way round. It's used in the
    animator, motion (StepPhase and the pace-change shift), footprints, footsteps and the foot-drift check, so all of
    them agree.
  - Fleeing or hunting animals gallop from 0.85× RunSpeed (`Sprint(state, speed)`, full by 1.15×). Player bodies
    are unchanged.
  - The body rocks ±0.07 rad once a stride (±0.12 for a bound).
  - Known limit: with stance at half the cycle there's no true all-feet-off suspension, and the existing sprint
    lift stands in for it.
- **Slot checks (the Z-52 and Z-53 checks are in the entries below):**
  - **Z-50:** a side `-camlook` of `-body Horse`, `-body Wolf` and `-body Rabbit` at full sprint, in 0.1 s bursts.
    Pass: the hind pair lands before the fore pair, the rabbit's pairs land together, and feet don't slide in a
    6-frame burst. Then `-spawn Deer:3` startled: the deer gallop, and the footprint spacing matches the feet.
    Run `-footdrift` if it's still in.
  - **Z-51:** count `CallsPlayed` around a town for 2 minutes and listen to the new sounds. A cat startled by a
    Startle lunge should hiss; a dog left after a Greet should whine; at night, a yowl and a dog howl.
  - **All:** 0 error CS / Burst / shader errors, no "Mesh bounds" lines, no rise in the spin log.

## 2026-11-01: Z-52 race stances coded (wip/z52-stance db78556, code only, not compiled)
- **DONE (code):** `CreatureSpecies.Regard` (species, -1..1, Rival) plus `RegardToAll`. FaunaDirector bakes each
  folk's regard and kinship into the blob by species index, and `CreatureBlob.StanceToward` hashes (individual,
  player species) to a fixed stance, so the host's brain and each machine's FolkTalkSystem agree with nothing
  sent (Individual is already a creature ghost field).
  - **Kin** (same base species) and **Warm**: always answer emotes (never shy), and now and then wave first at
    someone 1.5-6 m off in front of them.
  - **Rival:** answers any emote with a mock bow, bows first now and then, and teases.
  - **Wary:** doesn't answer emotes, and an idle one steps back to 4-5 m when the player is within 3 m.
  - **Annoyed:** walks off 5-8 m from an emote.
  - Odds: wary = -regard × 0.8; annoyed = -regard × 0.5 from -0.3. So humans toward a dwarf or elf player: 32%
    wary, 20% annoyed. Goblins and ogres toward each other: 40% / 25%.
- **Data (Zach):** Human → Dwarf and Elf -0.4 (one way, as he said it), Goblin ↔ Ogre -0.5 ("orcs" read as Ogre),
  Dwarf ↔ Elf Rival. Fish folk and cave pygmies don't exist yet, so they get RegardToAll 0 and -0.25 when added.
- **Dialogue:** lines take a `"stance"` key. The wary and annoyed say only stance lines (or nothing); kin, warm and
  rivals say them half the time. There are 27 new lines (Any: Kin and Warm; Human: Wary and Annoyed; Dwarf and Elf:
  Rival, including Bow and Wave answers; Goblin and Ogre: Wary and Annoyed).
- **Slot check (pass/fail):** 0 error CS / Burst. `-body Elf` in a dwarf hold and `-body Dwarf` in an elf grove:
  folk bow back to a Wave, and rival lines show. `-body Dwarf` in a human village: some folk back off and walk away
  from a Wave, and wary or annoyed lines show. `-body Human` in a human village: more waves back, kin lines. MP: a
  joined client in an Elf body in a dwarf hold sees the same bows (host brain) and lines (its own FolkTalk).
- **Wary/annoyed are about the player's body, not who they are:** a human player in a dwarf body gets the dwarf
  treatment.

## 2026-11-01: Z-53 chases coded (wip/z53-chase c0a9188, code only, not compiled)
- **DONE (code):** a land hunter's pounce at body length + 1.1 m lands half the time (always a miss inside a
  settlement). The prey sees the landed pounce (`Pounced`), is Gone (FaunaDirector removes it and counts it in
  `Caught`, as with a diving bird's fish), and the hunter eats head-down for 8-15 s, then rests 30-60 s (Hunger
  -0.6). A miss means Tired, and the prey runs on. The run opens with 1.25x RunSpeed for 3.5 s. The stalk is at
  0.5x walk speed and prey notices it at 12 m × alert (30 m once it runs). Prey (Hunted, Land) with a threat
  within 20 m jinks up to ±50° every 1-1.9 s. Hunt appeal is ×1.5 with prey within 20 m. Held, petted or Gone
  animals are never prey. New `-huntlog` (DebugControls): one line per stage change and how each hunt ended
  (ate / missed / slipped away / tired).
- **Correction to the design note:** the 1.25x burst (and the 1.4x lunge) is above Sprint's 1.05x start, so hunters
  partly gallop (sprint about 0.35) during the burst. Check that the legs don't slide; if they do, drop the burst
  to 1.04x.
- **Slot check (pass/fail):** build with world-features. `-place Dunes -spawn Coyote:1 -spawn Rabbit:3 -watch Coyote
  -huntlog`, with 0.5 s shots for 90 s (and RedFox/Rabbit in a meadow). Pass: 0 error CS / Burst; at least one
  "pounce landed" then "ended: ate" and one "missed" in the log; shots show the chase closing in frame and the hunter
  head-down after the catch; no spin-log rise (`-spincheck`); no "Mesh bounds" lines. Brain cost: townwalk
  `Brain.Predator` 2 ABBA captures vs c810406 (the jink is per fleeing prey only).
- **NEXT:** Z-52 (Zach's race stances, approved after Z-53) on wip/z52-stance.

## 2026-11-01: P3 design notes for OK (code-only, no Unity)
- **NEXT / needs OK:** DESIGN-animal-P3.md covers Z-50 gallop, Z-51 voices, Z-52 species reactions and Z-53 chases.
  For each it gives what the code does now, the proposal, the cost and a slot check. Suggested order: Z-53, Z-51,
  Z-50 (after Z-21), Z-52.
- **Z-53 finding:** chases do exist, but a hunt ends with nothing happening on contact (no catch), prey such as
  rabbits is about as fast as foxes and coyotes, and prey starts fleeing at 30 m, before the hunter's sprint at
  14 m. So chases are long, even and end off-screen.
- **Z-50 finding:** NPC quadrupeds never gallop (Sprint is only above RunSpeed, so players only), and the legs keep
  their trot phases even then.
- **Open questions for Zach:** may prey be eaten (vanish) in Z-53? Which races, if any, are cool toward each other
  in Z-52?

## 2026-10-31: slot 9: Z-20 remote interactions fixed: sent as an RPC (animal-behaviours c810406)
- **Root cause:** a joined player's request (Act, ActSerial, ActTarget) rode on NetPlayerCommand, an
  IInputComponentData. The client kept it set until the next request, so it wasn't a lost one-frame press. But the
  server copies input into the component only for its own current tick (`ApplyInputDataFromBufferJob`:
  `GetDataAtTick` on a 64-entry ring, the last tick of a step batch). If no entry is at or before that tick, it
  writes default (Act 0). With two Punchy on the laptop the host fell to 5-9 fps. The server runs 1 step per frame
  (batch 4), so its shared clock ran at 0.2-0.5x real time (slot 8: 43.9 to 45.8 s in 10 s), and the client
  logged "Large serverTick prediction error" all through. The client's ticks ran far ahead of the server's, so
  the PickUp ticks weren't sampled until the server caught up. Then PickUp and PutDown showed up in one batched
  frame, about 8.5 s late.
- **Fix (wip/net-interact 6d6d569):** a new `InteractRpc` (act, serial, ghost id, the player's position and yaw).
  The client's NetPlayerSendSystem sends it once per new InteractionRequest.Serial. InteractionApplySystem
  handles it on the server the frame it arrives (body from the command's Species, handled-serial dedupe kept). The
  three Act fields are gone from NetPlayerCommand; the pose still rides on it (continuous state, fine as input).
  Solo is unchanged (it reads the local request).
- **Interactions on that path:** all of InteractAct: Pet, PickUp, PutDown, Startle (the lunge). Every request
  from a joined client, and the host's own player too (the host's client world sends to its own server world). The
  game has no other client-to-server action (no punch or open yet). The other RPCs are GoInGame, Lightning and
  WorldCheck. Other things a player does (pose, emotes, sit, climb) are continuous state on the command.
- **v0.10.0 (519fb5b), from git alone:** same code path. NetPlayerCommand has Act/ActSerial/ActTarget, and
  InteractionApplySystem reads c.Act from the command. So remote pet, carry and lunge on a slow host are delayed
  or merged in v0.10.0 too.
- **Build:** a4a5378 (tmp/animal-slot9-net = wip/net-interact + world-features b39522b, scene seed 1337) built
  clean: 0 error CS, 0 Burst (BC) errors, 0 shader errors. The netcode generator made the InteractRpc serializer.
- **MP test PASS** (host `-host -body Human`, shot list `-spawn Cat:2` then `-watch Cat`; client
  `-join 127.0.0.1 -body Human -interact Cat:carry`):
  - s9-Cat: the host logged `act 2 ... 89 cm` and `Cat now Held` in the same shot line as the client's PickUp. The
    client saw `Cat is Held (holder 3); player pose Carry, body Carry` at 3 s (slot 8: holder 0, pose None). Client
    shot interact-Cat-carry-25 shows the cat in the arms.
  - s9b-Cat (0.5 s host shots): PickUp to Held within one shot (h-038 to h-039). The cat stayed Held until the
    client's PutDown about 8 s later, handled as its own request. **Host shot h-046 shows the joined
    player carrying the cat.**
  - s9-Dog: PickUp and PutDown now reach the host several seconds apart, as two requests. The dog wasn't lifted (Greet,
    over the 0.65 m limit), as before.
  - Host's own (s9-hostself, `-host -interact Cat:carry`): PickUp is handled the same frame, Held with pose Carry,
    and the PutDown follows. Unchanged.
  - 0 exceptions in all logs. Host fps this time was 26-60 with the client in (slot 8: 5-9), so a slow host wasn't
    reproduced. An RPC doesn't depend on tick sync, though.
- **Smoke a4a5378 vs b39522b:** no change beyond noise; it says NOT CLEAN only on townwalk@60 1% low 9.6->8.2. That's round order: round 1 5.2 vs 8.1 (new build first), round 2 11.1 vs 11.2. Both builds have ~500 ms worst frames at cap 60. Glide@60 1% low 5.6->6.8, townwalk@0 ~38-54 both. 0 exceptions. The solo path has no change but an empty RPC query.
- **Merged:** wip/net-interact into animal-behaviours as c810406 (pushed). cand/v0.11 is untouched.
- Shots: Punchy-wt/Tools/bench/results/animal-slot/s9-*, s9b-*.
- **Unity and Punchy are closed, the bench lock is clear, and the main checkout is on b/weather-mapjob.**

## 2026-10-31: slot 8: miners pass by sight; remote carry reaches the host about 8 s late (animal-behaviours c0ac39c)
- **Seed probe:** a batchmode editor method (not committed) built TerrainAuthoring.BuildSource for world seeds 1-15.
  Seed 1337 (ours) and seed 8 have a MineEntrance but no camp. Seed 3 has a HumanMiningCamp at (209,-3878) by its
  mine at (316,-3955). Seeds 1, 2, 4-7 and 9-15 have DwarfMiningCamps (seeds 4 and 7 have two). Probe builds: 866dc99 (77c03e9 + world-features b39522b + scene seed 3, local only) and 4c2f633
  (the same plus wip/remote-carry). Both built clean: 0 error CS, 0 Burst errors, 0 shader errors.
- **Z-22 miners PASS by sight** (s8-mine, seed 3, `-goto 209,-3878 -time 10`, then `-watch Human:Mine` per line).
  Miners walk from the camp to the mine with the pick on the shoulder, along the passage (stages 1, 11-13) to an ore face
  (101), swing there for about 45 s, then carry ore out (113-111, 199, 200). At the face: side-on, the pick is raised
  overhead in both hands (m-052) and brought down to hip height in front (m-042). From behind, the raise is straight up,
  facing the rock (m-049). The follow camera in narrow passages often ends up inside the miner's body (m-044, m-046).
  That's the test camera, not the pose.
- **Large animal (solo):** DesertTortoise (0.29-0.35 m, flee 0) holds still and is picked up (Held, pose Carry at
  3 s). Being wild, it squirms free after 2-5 s by design, so only the 2.5 s shot shows it in the arms
  (s8-tortoise). Dogs are mostly over the 0.65 m friendly limit (0.48-0.80 m). Cats are 0.29-0.36 m but wild when
  spawned, and they flee from the joining player.
- **Z-20 remote carrier: FAILS. The host gets the request late.** Runs: host `-host -body Dog` (or Human) with
  `-spawn X:2/4`, and a client `-join 127.0.0.1 -body Human -interact X:carry`. Notes:
  - The first run lacked `-host`, so it ran solo (the default) and the client dropped to solo.
  - With wip/remote-carry's logs, every run (Cat, DesertTortoise, Dog) shows the host handling the client's PickUp
    (ghost found, 0.9 m, folk 1) in the same frame as its PutDown, about 8.5 s after the client sent it.
  - In the Dog run, the dog was Held at the PutDown (31 cm, in the hands). So the pickup works, but only for that
    frame. Nothing is visible from the host's side.
  - The client never sees its own pose change (pose None).
  - Suspect: NetPlayerCommand is an IInputComponentData. Act and ActSerial look like they only reach the server's
    copy when the next request arrives. Next: log Act and ActSerial per tick on the server, or send the act as an
    RPC or InputEvent.
  - The cat runs also failed because the cat was fleeing (mode 2) when the request arrived.
- **Merged:** wip/remote-carry (86701ec, 20bb420: the host logs each joined player's interaction request; the client's
  `-interact` log adds the ghost id and distance) into animal-behaviours as c0ac39c. It built clean in 4c2f633.
- Test notes:
  - `-watch Human` on the host finds no remote player body: no "watching" line.
  - `-watch Dog` (host Human) works. A fixed `-camlook` on every line also works.
  - A joined client starts at the host's spot (0,65,0 on this island) and is placed 2.5 m off.
  - Burst's Debug.Log can't format `F1` (BC1343).
- Shots: Punchy-wt/Tools/bench/results/animal-slot/s8-* (s8-mine, s8-tortoise, s8b..d and s8-*-Dog/DesertTortoise
  host/client pairs).
- **Unity and Punchy are closed, the bench lock is clear, and the main checkout is on b/weather-mapjob.**

## 2026-10-31: slot 7: Z-07 Play spins fixed, bench paw prints fixed (animal-behaviours 77c03e9); remote carry not possible yet
- **Merged build:** animal-behaviours 9b7459b + world-features b870008 is the same tree as 5f4c917 (built clean in world
  slot 2). Smoke of c477c0f (that tree + both fixes, first Z-07 version) vs b870008: OK, no change beyond noise (1% lows
  townwalk@60 41.2->38.2, townwalk@0 45.9->45.8, glide@60 30.8->30.1, glide@0 28.5->33.4), 0 exceptions.
- **Z-07 cause:** two things in Play stage 1.
  - Past its range from the game's middle, the run was `lerp(away from the chaser, towards the middle, pull)`. With the
    chaser between it and the middle those point opposite ways and cancel at about range + 3 m. That's why the spin
    lines had focus 15 m (range 12) and target 8 m: the way to go was noise. Now the run's angle from the way in is
    narrowed, keeping the side it leans to (dead outward: its own side).
  - Blocked ahead (water, a steep bank), Flee and Play tossed a coin for the side every frame (target 20 m lines, also
    quail fleeing on a slope). Now: its lean side, the other if that's blocked, else back. Play also steers its own run
    off an obstacle (tries 45 deg steps, its own side first). A near miss in stage 0 (beside or behind, within 3 reaches,
    after 2 s) counts as a tag.
- **Z-07 counts (same spots, spin6 list, Dog body), 3 runs each:** 5f4c917 38 lines, 29 Play; a66c520 24 lines, 6 Play.
  The c477c0f build (first fix only) had 28/11: the outward-cancel lines went, the blocked coin toss stayed.
  **Merged wip/z07-play2 (960df87, includes 1257001's bow hold).** Left: Songbird and Dormouse Wander spins (focus
  ~900 m, target 3-17 m) and Dormouse Climb at 0.15 m, not Play.
- **Bench paw prints PASS by sight:** TraceSystem `Underfoot` now only takes a prop top up to 0.2 m above the feet.
  Human player at the bench (111.4, -404.8) with `-spawn Dog:4`: on 5f4c917 the seat is covered in dark prints (s7-bench2-5f4c917
  b-29); on a66c520 dogs go round and under it for 90 s and the seat stays clean (b-24, b-29). **Merged wip/trace-seat
  (6283358).**
- **Z-20 remote carry: not possible with the test hook.** Host Human `-interact Dog:carry` with a client `-body Dog`
  standing 3 m away (both see each other, s7-host/s7-client): the host never targets it. `-interact` only looks at
  creatures without PlayerAvatar, and carrying a player body isn't a feature. Needs a design call.
- animal-behaviours 77c03e9 + b870008 has the same tree as a66c520 (built clean, 0 error CS, 0 shader errors).
- `-camlook` in a shot list line with `-spawn` didn't take (camera stayed behind the player): frame bench shots from
  the player instead.
- Shots: Punchy-wt/Tools/bench/results/animal-slot/s7-*; sheets s7-bench2-old/new.jpg, s7-net.jpg.
- **Unity and Punchy are closed, the bench lock is clear, and the main checkout is on b/weather-mapjob.**

## 2026-10-31: slot 6 (part 2): wip/z28-air and wip/poses merged (animal-behaviours 9b7459b); Z-07 Play spins not fixed
- **Pass b (1e7fb47, built clean):** judged from results/animal-slot/s6-workb, s6-wormb, s6-host/s6-client.
  - **Z-22 PASS:** the woodcutter (chopb) lifts the axe two-handed and brings it down onto the block; the human
    smith (smithb) and the dwarf smith (dsmithb) raise the hammer and strike the anvil. The top of the human's head is
    cropped at 1.7,3, but the arms and the tool read clearly. Miners still can't be shot (no mining camp on this seed).
  - **Z-28 PASS:** with wip/z28-air the worms fly out straight. From above (torshigh-08/10/24) the body is a straight
    line, and in the side shots it arcs cleanly and dives back in. No flat curl on landing.
  - **Z-20 remote carrier: inconclusive.** `-interact Dog:carry` on the host never picked a dog up (interact-Dog-carry-*
    and host-* show dogs milling round its feet). The client did see the host's body and the dogs correctly.
- **Pass c (f0aacd5 = slot-6 tmp + z28-air + z07-play):** built clean (0 error CS). Smoke vs f5b6597: no change beyond
  noise (1% lows townwalk@60 36.9->38.9, glide@60 29.6->30.8).
  - **Z-07 spin count 15** at the same spots (f5b6597 before z07-play: 20; slot 5: 14). Play lines 12 vs 13: the stage-1
    circling (target 8-20 m, speed under 0.3, 180-300 deg/s) is still there for prairie dogs, rabbits, quail, trout,
    a swallow and a coyote. **wip/z07-play FAILS, not merged.** The bow and the side-step coin toss weren't the main cause.
  - Z-28 landing shots on f0aacd5 (s6-wormc): straight in the air, clean dive-in on the snow too.
- **Merged and pushed:** wip/z28-air (f116dd4) and wip/poses (9b7459b) into animal-behaviours. That exact tree wasn't
  built on its own; both are in the f0aacd5 build, which compiled clean.
- **Unity and Punchy are closed, the bench lock is clear, and the main checkout is on b/weather-mapjob.**
- NEXT: Z-07 Play stage-1 steering (look at why yaw rate saturates while speed is near 0 with the target 8-20 m away);
  Z-20 remote carrier needs a carry hook that actually picks up (or a Cat); miners need a camp.

## 2026-10-31: slot 6 (part 1): merged build compiles, Z-22 still not judged, worm coil and Z-07 Play spins found
- **Build:** tmp/animal-slot6 f5b6597 (animal-behaviours 4f2f0a4 + world-features 3976d17 + wip/poses 2755204):
  compiled clean. Smoke against 3976d17: nothing worse beyond noise, 0 exceptions. The J-006 glide floor flag
  predates this build (1% low went 26.4 to 29.6).
- **Z-22:** `-watchfrom 0.6,2.5` sits too low. Only legs and shadows are in frame (the shadows show a raise and
  strike). DwarfHold smith: the arm raises, then strikes. Too small to pass. wip/poses NOT merged.
  - No miners exist on this seed: DwarfMiningCamp and HumanMiningCamp aren't among the placed landmarks.
- **Z-20 large:** Monkey (0.38 m) is held on open forearms and looks right. Marmot is held too.
  - Both squirm free in under 5 s.
  - Turkey and RedFox fled (CanCarry needs a pettable animal of 0.5 m or less).
  - Remote carrier: the client's join timed out at the handshake (host at 3-10 fps while loading), so it fell back
    to solo. Retry with the client 45 s after the host.
- **Z-28:** found the flat curl. `Tunnel()` kept turning in the air towards a close target, so the worm orbited and
  coiled flat in mid-air (Tors torsside-23 and Glacier).
  - Fix: wip/z28-air ba7ff69 on animal-behaviours. There is no turning while Air > 0, so the arc also stays straight
    over the obstacle it was aimed to clear.
  - Not yet seen in a build.
  - Boulder routes from above (watchpitch 70): too close to read.
- **Z-07 Play (from -spincheck at PrairieDogTown, a lake and OakGrove; 20 lines):** two causes.
  - Stage 0 (target 0.3-0.65 m): the bow turns to track a partner running round it.
  - Stage 1 (target 8 or 20 m): blocked ahead, a coin toss of side every frame while Play re-aims at the obstacle.
  - Fix: wip/z07-play (holds heading through the bow; veers to one side). Compiled into tmp f0aacd5 only if pass c
    ran.
- **Pass b was still running at handback:** build 1e7fb47 (tmp + z28-air), then higher work close-ups
  (`-watchfrom 1.7,3`), the net retry and worm shots, into results/animal-slot/s6-workb, s6-host, s6-client and
  s6-wormb. Its script releases the lock and leaves the main checkout on b/weather-mapjob when it exits. Pass c
  (f0aacd5, spin re-check) has not started.

## 2026-10-30: slot 5: Z-07 fixed, worm and wake pairs, carry and worm arcs merged
- **Build:** world-features + animal-behaviours + spin-check + poses + carry + worm-arc + both pairs compiled clean,
  blocked-top included (zache-73: Blocked(..., out top) and RockAt build). Rebuilt with the Z-07 fix: clean.
- **Worm marks:** capped at 32 slots on both sides; the feeder reads `_PunchyBurrowRange` (DetailRange(BurrowRange)
  from GraphicsSettings), the shader gates on it. Marks and craters render from above. Cost not re-measured.
- **Z-07:** `-spincheck` at Start (Dog body) and ElfGrove 9/15/20 h logged 97 spins: elves in Home stage 0 and dormice
  in Climb stage 0, at 0.00 m from their target at full turn rate. The way to a target under 10 cm away is noise; and
  an elf's door beyond its leash was clamped to a spot it stood on. Fix (wip/z07-spin d337490): no turning within
  10 cm, no leash clamp in Home mode. Re-run: 14 lines, none of them elves or dormice; the rest is Play-mode
  circling (open).
- **Z-20:** cat and dog carried with the arms open under them, front-on and side-on, sprinting too. A cat squirms
  free after about 2 s (by design). Large animal and remote carrier not seen (open).
- **Z-21/Z-22:** sprint side-on looks fine; worker swings too small in frame to judge. wip/poses not merged.
- **Z-28:** clean breach arcs side-on; obstacle jumps and boulder routes not seen.
- **Merged and pushed:** animal-behaviours 4f2f0a4 (spin-check, z07-spin, carry, worm-trail, wake-trail);
  world-features 247c690 (worm-trail-shader, wake-trail-shader, worm-arc). Same push.
- Unity closed, no Punchy.exe left, lock released, main checkout back on b/weather-mapjob.

## 2026-10-30: Z-20 carried animals follow the arms: wip/carry 95d50e8 (code only)
- **Real fix, local on every machine, no network change.**
  - `CreatureState.CarryWidth` is local. HeldPoseSystem sets it on every carrier body from the held creature's
    half-width.
  - The Carry pose opens the arms by it: shoulders out up to 0.7 rad, elbows 1.25 to 0.83 rad.
  - HeldPoseSystem poses the carrier and puts the animal between the posed hand tips, so it moves with the arms.
  - Bodies without two arms keep the carry table.
- **To check in the slot:** the opening's direction, the sit along the forearms, and small, medium and large animals
  side-on and front-on, plus a remote carrier from a client.

## 2026-10-30: Z-07 spin readout, Z-21 and Z-22 poses (code only), Z-20 needs a design call
- **Z-07 readout, wip/spin-check 494ada9 (debug only):** `-spincheck` logs any creature that turns a whole circle
  within 5 s while under 0.3 m/s, with what it's doing and where. `-watch` lines now give yaw and turn rate.
- **Z-21, wip/poses 2755204:** a sprint amplified every leg joint, the ankle included, so the foot folded against the
  shin at push-off. Now only the hip and knee get the longer stride; the foot keeps its run angles, eased 35% toward
  rest.
- **Z-22, wip/poses 2755204:** chopping, hammering and mining now have a slow raise with a hold at the top (the
  wind-up), then a fast strike.
  - The torso leans back and twists away while raising, folds forward into the blow, and the head drops.
  - The hammer's movement is under half the axe's.
  - Legs are untouched, so the feet stay planted.
- **Z-20: needs a design call.** HeldPoseSystem puts the animal at per-body carry-table hand spots, but the carrier's
  Carry arm pose is fixed. To fit the arms, the carrier's state needs the held animal's half-width. The options are
  that, or fixed arm poses in three size classes.
- All need a slot to compile and shoot.

## 2026-10-30: Unity slot 15:35-16:10: v0.10 fixes compile and merge, Z-06 confirmed, Z-07 not reproduced
- **Compile PASS** on world-features 7f519c0 plus animal-behaviours, wip/v010-fixes, wip/gfx-creatures and both
  wake-trail halves (test build 761dae5). No compile or shader errors, and no exceptions in any run.
  - So gfx-creatures compiles against the real GraphicsSettings.
- **Z-06: PASS.** The player's Bow emote as a horse, a deer and a dog, side-on in a meadow: the chest dips, the front
  legs bend, and the hind feet stay planted.
- **Z-07: not reproduced.**
  - HumanVillage at 19:30, a Human watched for 75 s: it walked home, then the watch picked another Human walking to a
    seat 113 m off.
  - Four spawned squirrels in an OakGrove for 50 s: they climbed, with no spin.
  - Still open. A faster repro needs a yaw-rate readout in the -watch log line, or Zach's spot and time.
- **Z-27 wakes: renders.** Spawned ducks on a lake show the spreading rings round each one. The mask path compiled.
- **Merged:** wip/v010-fixes into animal-behaviours at d0388e2, pushed. That's Z-02 (player side), Z-03, Z-04, Z-05,
  Z-06, Z-08, Z-14 and Z-23 to Z-26.
- **Not merged:**
  - wip/gfx-creatures 6f6dd94: it sits on the world lane's gfx branch, so it belongs in world-features. It merges
    clean there, and compiled in this slot.
  - wip/wake-trail plus wip/wake-trail-shader: they wait for the profiler's cost check (item 17).
  - The worm trail is waiting on item 17 too.
- **Not done:** Z-20, Z-21 and Z-22 pose shots; those rows aren't coded yet.
- **Unity closed** at about 16:10. No Unity or Punchy processes, the lock is clear, and the main checkout is as found.

## 2026-10-30: v0.10 feedback Z-14 and P2 (wip/v010-fixes, wip/wake-trail), code only
All code-checked only, none compiled; they need a slot.
- **Z-14, 2ab65e4:** the wolves' howl is softer, distant and once a night per pack.
  - Volume 1.0 to 0.5.
  - Synth: breath layer halved (the static), gentler vibrato, and a baked 1.2 s echo tail (`SoundSynth.Distant`).
  - GameAudio (shared file): every voice now has a distance low-pass, open to 40 m and down to 1.8 kHz by 200 m.
  - Each pack howls in one half-hour window a night, 21:00-03:00, keyed by its home (`HowlTime`). Before, a big pack
    could pass the chorus round all night.
- **Z-23, 1904241:** each body jumps its own height. `CreatureSpecies.PlayerJump`, else from size
  (sqrt, 0.45-1.4) with climbers 1.25x.
- **Z-24, ce4d3d7:** new `SoundId.Rattle` (appended). The rattlesnake had no calls at all; it now rattles by
  day, and a player rattlesnake rattles with every emote.
- **Z-25, 7e9ce49:** a ghost player can talk to ghosts, and they don't vanish from it.
  - Animals don't flee a player of their own species.
  - Ghosts talk only to a ghost player.
  - New Ghost culture in dialogue.json.
- **Z-26, 15e972c:**
  - The moo now rises and opens from a hum; it used to fall, which read as a groan.
  - The croak is rebuilt as clean pulse trains, "rib-bit", in place of the noisy vibrato that sounded like a burp.
  - New `SoundId.FrogChorus`: a night hum looped from among frogs within 40 m, louder the more there are.
- **Z-27, wip/wake-trail 34e2bdb plus world half wip/wake-trail-shader 40cd4b7** (Water.shader, for zache-73 to
  review). Swimmers drop ripples every 0.5 m or 0.5 s. Each spreads as a ring from where it was made over 3 s, so the
  wake is left in the water. 48 slots, 5 m reach. As with the worm trail, the two halves must land together.
- **Still to do:** Z-20 (held animals following the arms), Z-21 (foot against the shin in the long run stride) and
  Z-22 (putting the body into chopping and hammering). These are pose work and best tuned with shots in my slot.
- **Fingerprint:** no network-table changes. The new SoundIds are appended; the rattlesnake asset gains a call.

## 2026-10-30: v0.10 feedback P1 (wip/v010-fixes) and creature graphics ranges (wip/gfx-creatures), code only
All code-checked only, so none of it has compiled yet. They need a slot build.
- **Z-02 (player side), f378a43:** in a cave, a flight step that would enter rock was cancelled whole, so flying at a
  wall the bird hung in mid-air. That is the likely player-side cause. It now slides along level if that's clear,
  else sinks until it's down. Not reproduced; the cave side is zache-73's.
- **Z-03, 30c809c:** "swimming" now needs the player down at swimming height, so a jump over a river is neither
  slowed nor carried by the current.
- **Z-04, 30c809c:** the player fish's leap left it just under the surface line the swim branch tests, so the next
  frame zeroed its rise. It now stays airborne while rising.
- **Z-05, 30c809c:** player bodies (Puppets) skip the timed-call clock, so they vocalise only by emoting.
- **Z-06, 30c809c:** an animal body's Bow emote (a play bow, which reads as drinking) tipped about the middle, so the
  hind feet lifted. G-12 had fixed only the lowered head (HeadDown).
  - Now the bow also tips about the hips.
  - Hoofed bodies asked to sit lie down with their legs folded instead.
- **Z-07: needs a repro in a slot.** Reading the code found no clear spin in the sit stages or fidgets.
  - Next slot: watch hearth and seat sitters and a squirrel.
  - Suspects: an unreachable sit spot, where it orbits the target until the 60 s give-up, then picks it again; or the
    target sitting under it.
- **Z-08, f96056c:** breath now comes from each body's own mouth.
  - `CreatureData.MeasureMouth` measures the end of the head in the idle pose at bake.
  - Players' bodies now breathe in `CreatureBreathSystem` too.
  - AmbienceSystem (world lane's file, 2 lines) skips its fixed human-height player breath while players have bodies.
    That is the likely cause of the chicken player's breath above its head.
- **Graphics settings, wip/gfx-creatures 6f6dd94** (off 9cf4e26): the ranges below are now
  `GraphicsSettings.DetailRange(old value)`, so nothing changes at 1.0.
  - CreatureRenderSystem: foot detail 40 (passed into PoseJob, since Burst can't read the setting), torch flame 150,
    lantern 60, colonies 45, cave fish 35, swarm drawing 40.
  - TraceSystem: 40.
  - Swarm anger and timings stay fixed. BehindCull is left alone.
  - The burrow feeder is wired when wip/worm-trail lands with it.
  - "F5 graphics / quit" added to the F1 key line.
- **Fingerprint:** no network table changes. CreatureBlob gains Mouth/HasMouth (baked locally).

## 2026-10-30: Unity slot 08:20-09:05: climbing passes, worm trail of bumps (Zach's request)
- **Compile PASS:** worm-look, trunk-lean and the world worm shader, together.
- **Monkey climbs: PASS on oak and palm,** with client shots.
  - Oak: hands and feet are on the bark low down, and it hugs the drawn trunk at its leaning top.
  - Palm: it stays on the bark through the bend and at the crown. Find one with `-place Dunes -place tree:Palm`;
    no palm stands alone near the start.
- **Hold logs:** every hold is on the east side, the side it walks in from, 0.0 m from the logged spot. The far-side
  climbing seen in the last slot is gone.
- **Snow worm band fix: PASS.** The body reads as one even pale colour.
- **Merged into animal-behaviours and pushed:** fade08c (wip/trunk-lean) and d83b1ee (wip/worm-look). The tree is
  identical to the tested build.
- **NPC lean:** not looked at. It's still a straight post for animals; your call after these shots.
- **NEW, from Zach mid-slot.** The worm's trail turned with it (he wants separate bumps left in the ground), it broke
  out a few metres from its crater, it vanished before it was all back under, and its marks went too soon.
  - **wip/worm-trail 93c79bc, the animal half:**
    - `CreatureBurrowSystem` leaves a bump every 2 radii, kept 30 s, plus the mound over the head. Nothing carries a
      heading any more.
    - Craters are kept 40 s and placed where the body crosses the ground. They used to go where the arc began, under
      the ground and metres back.
    - The arc in `Tunnel` starts and ends 0.8 of the body's length deep, so the worm only levels out once it's all
      under. About 3 s of it is still above the ground.
    - Thrown sand and snow lands and lies 25-40 s as clods (`Trace.Rests`). Water splashes are unchanged.
  - **wip/worm-trail-shader 3338e66, the world half:** one commit on world-features for zache-73 to review.
    `BurrowMarks` reads 64 slots of (x, age, z, ±radius), draws each bump as a soft hump that sinks away late in its
    life, and drops the furrow. Craters are as in ad98690.
  - **Slot check: PASS** (build of both halves; shots in `results/animal-slot/trail`).
    - A row of separate bumps stays where it was raised.
    - The worm comes up through its crater and dives back into it. Its body goes all the way in, and clods lie round
      the hole.
    - Craters and trails are still there a minute on.
  - **Shader cost (for the profiler, items 15/16):** while any mark is within 250 m of the camera, every
    TerrainSurface fragment loops over up to 64 marks.
    - Each mark costs a float4 load and a squared-distance test against 3 radii, and nearly all are rejected there.
      Only a fragment inside a mark does about 15 more ALU (an exp, plus a length and a second exp for a crater).
    - In the worst case, with a worm in view and its full trail, that's 64 iterations per terrain fragment. With no
      worms near, the loop is skipped whole (`_PunchyBurrowerCount` is 0).
    - It used to be 8 slots with a 10-radius reach. If it shows up, the cheap fixes are 32 slots or a per-chunk
      bounds test.
  - **Not merged:** the feeder and the shader must land together. The new feeder against the old 8-slot shader
    would misread the slots.
- **Unity closed** at 08:58 (bench timeout). There are no Unity or Punchy processes, the lock is clear, and the main
  checkout is as found.

## 2026-10-30: climbers follow a leaning trunk: wip/trunk-lean 26ddffe (code-checked only)
- **DONE, code only.** The player now grabs and climbs round the trunk's centre at its own height, not a straight post
  at the foot. That covers Broadleaf lean and wiggle, Conifer lean and Palm bend.
- **Flora change, minimal and additive:**
  - `FloraModel.TrunkLeanMid/TrunkLeanTop` hold the lean at half and full height. They're filled from a new
    `Grower.TrunkAxis`, which Broadleaf, Conifer and Palm set from the centreline they already build.
  - `TrunkAxis` is separate from `TrunkPath` because `TrunkPath` switches on brackets and bark faces, which draw
    from the seed.
  - `FloraObstacle.LeanMid/LeanTop` are turned and scaled the way `Flora.hlsl` draws the tree, with a new
    `FloraObstacle.CentreAt`.
  - No new random draws; meshes and placement are untouched, so the world and fingerprint are unchanged.
- **Player side:** `PlayerMotion.TrunkLeanMid/Top`, `PlayerMovementSystem.TrunkCentre`, GrabTrunk and ClimbTrunk.
  The `-autowalk` climb test pushes at the centre too. Nothing on the wire.
- Told zache-73 the files and lines. It trial-merges clean with world-features and with world-laptop/f40.
- Not covered: NPC climbers. Their tree spot comes from `FloraGenerator.Shape`, which grows no mesh, and CreatureState
  is replicated, so carrying a lean there means a FloraShape field filled while baking the blob. That's a follow-up
  if you want it.
- Next slot (with wip/worm-look): `-body Monkey -place tree:Oak -autowalk`, with client shots as before and a palm
  (`-place tree:<Palm species>`). Pass = it hugs the trunk as drawn high up and doesn't drift off a leaning trunk.
  The new hold log (on worm-look) will also show which side it takes.
- NEXT: idle until the slot.

## 2026-10-30: trunk-hug merged (2272733); worm band, far-side climb and a still camera (wip/worm-look 85a1962)
- **DONE: wip/trunk-hug merged into animal-behaviours at 2272733, pushed.**
- **1. Dark band on the worm: the body's colouring, not shading.** Both worms reuse the `SnakeBody` limb, where every
  other segment takes the coat's Accent colour (a snake's bands). The snow worm's accent was dark blue-grey (0.55,
  0.6, 0.7 on a base of 0.82, 0.85, 0.9), and the sand worm's was dark brown. That gives hard-edged rings square to
  the body (worm-026/027/028); the edges are segment joins. I ruled out the shader's darkening: it's only a 12 cm
  contact band at the creature's own height, plus downward faces. Fix (c751907): each accent is now a shade off its
  base, so the segments read as rings. Colours only; nothing on the wire.
- **2. Far-side climb: the collider isn't offset at the base, but the drawn trunk drifts away from it higher up.**
  - The collider (`FloraSystem` obstacles) and the tree's base come from the same `FloraPlacement.Place` position.
  - The drawn trunk leans and wiggles above the base. Broadleaf: a lean of up to 7% of height × t² (up to about 0.7 m
    at the top of a 10 m oak) plus a wiggle of about 0.6 × trunk radius per section. Conifer: up to 3%, linear.
    Palm: a bend of 8–25% of height.
  - The climber follows a straight cylinder around the base, so on a leaning tree it hangs off the drawn trunk higher
    up. That also accounts for part of the gate's gap.
  - It doesn't explain which side the player climbs: Net status put it about 0.3 m+ on the far side of the base.
    The climb test now logs each new hold: the trunk's position against the logged spot, its radius, and which side
    the player is on. The next slot will settle it.
  - Proposed fix, needs your go because it touches the Flora files (world lane): FloraModel keeps the trunk's
    centre offset at half and full height (the generators already have `TrunkPath`), FloraObstacle carries it turned
    and scaled, and ClimbTrunk (plus the NPC tree spot) puts the climber round the trunk's centre at its own height.
    Local data only; no network change.
- **3. Still camera for the joint look: `-watchfrom H,D` (85a1962, debug-only).** Used with `-watch`, the camera
  stands D m to the side of the animal's heading and H m above the ground there. It turns to keep the animal in view
  (for a buried worm, the ground over it), stays put across the shot list's lines, and moves only when the animal
  gets more than 2D away. For the next slot: `-place Glacier -spawn SnowWorm:1 -watch SnowWorm -watchfrom 10,20` on
  the first line, then `-watch SnowWorm -watchfrom 10,20 -wait 1` per shot. Pass = the furrow and mound show from
  above, and the crater stays where the worm broke out.
- Code-checked only. The next slot compiles wip/worm-look; it can merge after that.
- NEXT: idle until the slot (after the world lane and the profiler's item 14), or your go on the trunk-lean fix.

## 2026-10-30 06:09: Unity slot (05:38-06:08): worms and trunk-hug checked in builds; Unity closed
Built through the runner's `build()` on the main checkout, then put it back (b/weather-mapjob, clean). Test commit
`t/animal-slot` (local only) = wip/trunk-hug + the world lane's worm shader 89ae558, cherry-picked. Shots and contact
sheets: `Punchy-wt/Tools/bench/results/animal-slot/<run>/` (worm, climb-host/-client, climb-*-1 = the first build, npc).
1. **Compile: PASS.** 5192485, then eeb373e: 0 errors, no warnings in the files I touched. Includes 149e4b0.
2. **Snow worm: PASS.** `-place Glacier -spawn SnowWorm:2 -watch SnowWorm`, 121 shots, 0 exceptions, no NaN-bounds
   lines. Two breaches about 16 s apart: it rises about 8 m in an arc and dives back. Snow puffs and a small crater
   show where it breaks out (worm-026).
3. **Joint look (for the world lane), partial:**
   - The breach crater shows at the break-out point (worm-026).
   - The big round holes in worm-009/013/015/060/090 are probably the Glacier: Grotto cave's entrance, not worm
     marks; the run started at that cave.
   - Not captured: the mound and furrow under the snow. `-watch` follows the buried worm, so the camera keeps going
     under the ground (worm-005/024/029).
   - Two oddities: a hard-edged dark band on the worm's body in mid-arc (worm-027/028), and the body's segments
     looking apart under the ground (worm-029, seen only from below).
   - A proper look needs a fixed camera over flat snow away from caves.
4. **wip/trunk-hug: PASS on contact, one test-hook bug found and fixed.**
   - **Hook bug, first build:** the test player stayed at the top of the trunk on its far side all run. The hook
     steered at the trunk spot it logged, which is a few tenths of a metre off the collider. With the feet on the
     bark, that was enough sideways push to walk it round the trunk, and from there heading home climbed. Fixed in
     80f2c22 (debug-only): up a trunk it pushes straight at, or straight away from, the trunk it holds.
   - **Second build:** Net status alternates between the top and the foot every 10 s, so it climbs and comes down
     again. The client's shots (climb-00/09/36) show the player Monkey with its paws and feet on the bark, where
     the gate had a half-metre gap.
   - Still open: it climbs on the far side of the trunk from where it starts (x ≈ -268 against a logged trunk at
     -267.2). That fits a trunk collider offset from the logged spot, but I haven't confirmed it; a log line of
     TrunkBase would. Whether it pops at the foot can't be judged from 2 s shots; that needs a short burst at about
     0.2 s.
   - **NPC climbers:** solo `-spawn Monkey:4 -watch Monkey` caught one going up (npc-53..56) and one coming down
     (npc-23..26). Feet at the trunk's edge, no visible gap, although depth is ambiguous from that angle.
- wip/trunk-hug is 6d231af, f6fb2dd, 80f2c22. Ready to merge into animal-behaviours on your go; the far-side question
  and the foot-pop burst can follow.
- **Unity closed:** no Unity, Punchy or bee_backend processes, and the bench lock is clear.
- NEXT: idle.

## 2026-10-30: NPC climbers on wip/trunk-hug too (f6fb2dd)
- **DONE, code-checked only:** `CreatureBrainSystem.Climb` now puts an NPC climber's feet on the bark while it's up the
  trunk, the same as the player's. An NPC has no collision radius, so at the foot (and out on a branch) it keeps the
  old stand-off of 0.8 × stand height instead; that stops its chest poking into the trunk while it stands facing it.
  It eases between the two over its first stand height going up and its last one coming down, and as it pitches
  upright. Clients see it through the ghost position, as before.
- My slot plan, after F-40 and item 13: compile, snow-worm check, joint look with the world lane, then the trunk-hug
  shots: the player Monkey with the client watching, plus any NPC monkey or squirrel climbing near by. Pass = paws and
  feet on the bark, no pop at the foot. If no NPC climbs in view I'll find a monkey group by hand (there's no hook to
  make one climb).
- NEXT: idle until the slot.

## 2026-10-30 (after v0.10.0): worms merged into animal-behaviours (149e4b0); monkey trunk gap on wip/trunk-hug (6d231af)
- **DONE: wip/worms (incl. crater fix 7cb0674) merged into animal-behaviours at 149e4b0, pushed.** The conflicts in
  CreatureData.cs and CreatureSpecies.cs were the two new fields next to each other (EmoteReaction, Tunnels); I kept
  both. Also put TurnTowards' doc line back above TurnTowards (the worm branch had left it above Tunnel's). Not
  compiled yet (no slot). Still waiting for a slot: snow-worm check, joint look with world (crater fade by `1 - b.y`),
  fingerprint go for v0.11.
- **Monkey off the trunk: diagnosed, fix on `wip/trunk-hug` 6d231af (off 149e4b0), code-checked only.** In climb-11
  the body is about 1 m out from the trunk's centre, leaving a gap of about half a metre to the bark. It's an outward
  gap on the approach side, not a sideways one; it only looks sideways because the client's camera views the climb
  from the side. Causes:
  1. `ClimbTrunk` held the feet 0.6 × 0.35 m out from the bark, and the animator hangs the body a leg's length out
     from the feet.
  2. The player's body is a `Puppet`, so while climbing its front paws tucked up like "sitting up tall" instead of
     holding on the way a climbing NPC's do.
  Fix: the feet go onto the bark, eased in over about 0.2 s. When it lets go at the foot, the body steps back to the
  collision radius, so collision doesn't shove it out. A puppet keeps its front paws out while climbing. Other players
  see the same, since it's their transform and the same pose. Upright folk aren't affected (the fold only applies to
  non-upright legs).
- To check (next slot, any order): `-body Monkey -place tree:<X> -autowalk` with the same client `-camlook` shots.
  Pass: the paws and feet touch the bark, there's no visible gap, and there's no pop when it steps off at the foot.
- Seen while reading, not changed: NPC climbers (`CreatureBrainSystem.Climb`) hold their feet 0.8 × stand height off
  the bark. By the same reading that's an even bigger gap. Worth an NPC monkey or squirrel in the same shot.
- wip/brain-sample 7bd0768: agreed. A merge would keep the hooks (the two-dot diff only shows 3025dbf missing from it),
  but I'll merge animal-behaviours into it after item 13's capture so the hash being measured doesn't change.
- NEXT: idle until a slot or the next task.

## 2026-10-30 02:05: test hooks merged into animal-behaviours (3025dbf); worm craters on wip/worms (7cb0674)
- Profiler run of 0019e8a: tree PASS. Ledge: the drop-back worked (Net status alternates top/start every 10 s), but
  the pick was a thatch eave. Fixed in b7d82cd: flat tops only (level within 15 cm, 1.2 m in, ±0.5 m), a sloping one
  only as a fallback; the log says "flat top" or "sloping top". Merged; the rc build compiles it.
- wip/worms 7cb0674: craters fixed at break-out and dive-in, 6 s, age in y (world fades by `1 - b.y`). Not merged.
- Post-v0.10 note: the climbing monkey hangs slightly off the trunk's side.
- NEXT: idle until the supervisor's next task.

## 2026-10-30 02:30: worms review (post-v0.10): what wip/worms and wip/world-worms each need before merging
Code read only. The contract matches on both sides (`_PunchyBurrowers[8]` = x, yaw, z, ±radius; `_PunchyBurrowerCount`
via SetGlobalInteger). Each half is inert without the other (count 0 → shader branch skipped; no shader → no marks),
so either can land first. Trial merges: wip/world-worms into world-features clean; the two worm branches touch
different files.
- **Why the crater "barely shows" is mostly the animal side's fault:** the feeder sends w < 0 only while
  `Air > 0`, at the worm's *current* position. So the crater rides along the 4 s, ~30 m breach arc under the worm's
  own body and is gone the moment it dives. There's no fixed mark where it broke out or where it went back in.
- **wip/worms needs:**
  1. Merge animal-behaviours: 2 trivial conflicts (CreatureData.cs, CreatureSpecies.cs: Tunnels next to the new
     EmoteReaction; keep both).
  2. Feeder fix: when a worm's Air goes 0 → >0, record a crater at that spot, and another where it dives back in.
     Keep each about 6 s and send its age 0..1 in y, which the shader ignores for craters today, so this is
     compatible. The 8 slots are shared, nearest first. It's worked out per machine from Air, which is on the ghost
     (CreatureGhosts), so clients see it too; a late joiner misses old craters (cosmetic).
  3. Check in play: the snow worm (never seen yet); whether the mound should sit at the head rather than the body's
     middle (Position is the body centre, and these are long, size-16-20 bodies).
  4. Supervisor's go (rule 11): two new species move the species table and the world fingerprint (incompatible
     with v0.10). Plus a smoke bench; the feeder is a plain loop over the creatures, no sampling, so it should be
     cheap.
- **wip/world-worms needs:**
  1. Strengthen the marks (its own note). Mound and furrow tilt peak at about 0.25-0.3 and darken at ≤ 0.28.
  2. Once (2) above lands, fade the crater by age (`b.y` when `b.w < 0`) and draw it at the fixed point.
  3. Optional: the furrow fades over 2-8 radii. "About 4 body lengths" in its comment is really 4 diameters, and
     these bodies are much longer than that.
  4. Check one fixed shot with the animal half merged in.
- Order I'd suggest after v0.10: animal feeder fix → world strengthens and fades against it → one joint look → merge
  each into its own lane branch.

## 2026-10-30 02:10: task 2, Brain.Advance split: DONE code-only (not merged)
- `wip/brain-sample` 7bd0768 (off animal-behaviours) and its twin `wip/brain-sample-e4010f2` 6cfedaf (never merge).
- Inside Brain.Advance: Brain.Sample (ground/water/cave/wall sampling), Brain.Phase (gait step), Brain.Moving (empty
  mark per moving creature: callsPerFrame = movers). Advance self = the rest.
- Reading: more Moving calls with Sample/call flat = more movers (Accelerate); Sample/call up = dearer sampling.
- NEXT: waiting on the profiler's run of wip/test-hooks 0019e8a; then merge it to animal-behaviours.

## 2026-10-30 01:55: task 1, test hooks: DONE code-only, wip/test-hooks 0019e8a (not merged)
- `-place tree:<species>` now faces the trunk. New `-place ledge`: to the nearest settlement, then (once props' tops
  load) on level ground 1.5 m in front of the nearest rim the body can mantle (about 1-1.8 m for a human), facing it.
- `-autowalk` with either: 12 s at the trunk or over the rim (jumping near a rim), 8 s back, repeating every 20 s.
- Both log `DebugControls: climb test: ...` with the trunk/rim position and ready `-goto`/`-camlook` client flags.
- Not compiled or run by me (no editor slot); the profiler builds and runs `-body Monkey -place tree:Oak -autowalk`
  and `-place ledge -autowalk`. Merge to animal-behaviours after both pass.
- NEXT: task 2 (Brain.Sample marker plus a moving count), code-only.

## 2026-11-03: batch A-07, animal-behaviours 32f90e4 (one build, Builds/Windows)
- #46: fear distance was already 3D. Now the height gap past 1.5 m counts x2.5 (level fear unchanged). `-flatfear` = old.
  Hover log (fleeing/creatures within 40 m, 3 samples): after 5 m 5,4,0; 15 m 1,0,4,3; 30 m 6,3,0. Before 5 m 0,4,2;
  15 m 0,7,4,4; 30 m 5,6,5. The log counts every creature near, not just the spawned deer: inconclusive.
- #43: table already in (A-03). Calls per minute (x6 of 10 s windows), before (all biomes, 1.5/s) vs after (table, 0.6/s):
  Meadow 96-132 -> 36-60; Dunes 0 -> 0; Glacier 0-12 -> 0. New flags `-calllog -nobiometable -callrate N`.
- #33: `-place wall` now prefers a flat top (1.3 m boulder); Lizard tops out, OnWall False, grounded at the top, no
  stick. New release: on a wall with feet on a top and no push down, it lets go. Sideways was 0.9 m/s in the probe.
- NEXT: filter the #46 log to spawned deer; #33 run on a garden wall.

## A-10 (2026-11-03): carnivorous plant, #41
- New `Mantrap.cs` (animal-behaviours fb3f028): rooted plant, 6-joint stalk, four leaves, two black dome jaws with red lining and teeth; head tracks the nearest living thing within 8 m, lunges (wind-up, strike, snap, 1.2 s cooldown) within 1 m. Hit = shove (player: push + hop; animal: shoved and set fleeing); no damage system exists.
- Placement: seeded survey, every peer builds the same plants; head simulated locally. 26 plants: FoggyMarsh 10, PoisonSwamp 8, FungalForest 8 (candidates 326/372/311; 10 rejected for settlement or landmark).
- 60 s test (`-mantraptest`): plant 0 tracked the whole time (3264 frames); 4 lunges (1 Deer walk-past, 3 on the player). Animal hit: shoved and fled. Player hit NOT confirmed: `-autowalk` moved the player past 1.6 m before the snap landed (3 of 3 logged "out of reach"; head tip 1.2-1.6 m from target).
- Shots in PunchyMilestones/animal/a10: plant-lunge shows the plant (leaves, stalk) but the head is hidden in foliage; plant-side is badly framed (fog, plant mostly out of frame). Need a reframe.
- NEXT: hit test against the target's position at the strike start (reach 2.4 m), not at the snap; frame on the head; check client view.
