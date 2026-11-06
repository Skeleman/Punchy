# Animal reports (animal lane → supervisor kind-possum)

Newest first.

## 2026-11-04 batch A-68 (PARTIAL, paused for Zach's test build): row 1 done, rows 2 and 3 left
- DONE row 1 #132: exact wall-check skips in FloraPlacement.Blocked (prop spot beyond its walls' reach) and dock lane / wharf distance (dock whose slid stretch is past the caller's limit skips its DockShore walk) (animal-behaviours 3d66652). v2 fishing village 3 (-2883,-2121, noon, 121-126 creatures, editor uncapped): brain ms 4.92 -> 3.29, advance 4.05 -> 2.72, solids 3.62 -> 2.49, frame 11.6 -> 9.7 ms (about 86 -> 102 fps). `-oldwalls` = old path for A/B. Commented on #132.
- Fingerprint: code-only skips with the same answers; run printed C1D6A1EA (not 8726AADB) only because the row 2 BatFolk.asset edit was imported in the editor.
- LEFT row 2 #35: BatFolk.asset (hair caps removed, ears x1.4 and re-socketed, coats x1.35) and LightNear (hearth/camp fire or a torch/lantern carrier within 4 m) are on disk in the animal tree, uncommitted and not yet shot.
- LEFT row 3 #35: pygmies back into the bat folk chamber with shared Decide entries and anchor-name hooks (not started).
- White circle in the building-walk test: not found in my editor; my A-68 runs took no shots (the shot dir was missing), so I could not check it. Probably another lane's test setup; still open.
- Weak: fps measured in the editor, not the build bench; solids still about 2.5 ms in that village (settlement-plot Place in BuildingSink). Editor closed.

## 2026-11-02 batch A-42: skunk flee gas, goblin arms halfway in, lying legs fold above ground; gas not visible in the shots yet
- DONE: skunk puffs green gas on each flee start plus a 2.5 s wind trail, 10 s cooldown, one pooled system, no network fields (animal-behaviours 237d226)
- DONE: goblin arms pulled about halfway to neutral (HumanArm direction 180 -> 155 on Goblin.asset): shoulder back-angle idle 58.7 -> 33.7 deg, walk mean 54.1 -> 29.2 deg (range 27.2..81.0 -> 2.6..56.1). Human unchanged (8.7 / 8.9).
- DONE: lying quadrupeds fold each leg about its hip until it clears the ground plane under it (CreatureAnimator; `-oldlie` = old pose). Probe, all 4-legged land species, Rest + Sleep, flat / 20 deg ahead / 20 deg side: legs more than 0.05 m under ground 297 -> 9. Flat: Deer +0.033, Dog +0.012, Skunk +0.007, Horse +0.012, Camel +0.015 m (before Deer -0.84, Horse -1.19, Camel -1.25, Dog -0.47, Skunk -0.19).
- NOT DONE: 9 cases on the 20 deg side slope still -0.050..-0.075 m (tortoise/turtle Rest+Sleep, Horse, Jackrabbit, SandHare, MountainLion): the uphill hip socket sits inside the flank, the leg chain is clear. Fix idea: raise the lying body by cross-slope x half width.
- NOT DONE: gas is logged (2 puffs in 2 scripted scares, peak 48 particles, 0.005 ms/frame avg, 2.2 ms max on the first-emit frame) but no green cloud shows in skunk-flee / skunk-2s (on snow). Needs one editor look: particle size/alpha or PuffMaterial tint. Lie after-shots lost framing (camera did not stay on the respawned specimen), so 1e is not shown well.
- Checks: compile clean; 1 build (66 s, 0 errors); player log hash v2 126 / 053C63D5; no exceptions grepped; line endings kept (CRLF on CreatureAnimator/Data/Species).
- Shots: PunchyMilestones/animal/a42/P-gob-idle.png (goblin idle before|after, small difference at this distance); P-gob-walk.png (walk before|after); P-skunk.png (flee moment | 2 s later, no visible gas)
- Over budget: about 110 calls (ceiling 85), 1 build. Why: probe iterations on the fold, an editor frame-pick run whose shots never saved (shot dir missing, -pose reset per line) and the summary/restart.
- NEXT: make the gas visible (bigger/denser puffs, check material tint), reframe lie shots (one specimen per run, `-pose X:lie` repeated per line), side-slope hip lift.

## 2026-11-02 batch A-20: built dogs see hares and growl; calm child carry still fails in the build
- DONE: #7 dogs in a built player now hunt hares: species prey matched by name, not object (Resources copies broke it) (animal-behaviours e2eda9a)
- NOT DONE: #7 side-on chase shot with the hare in frame: -huntside 6 caught a Wolf; the dog shot has no hare
- NOT DONE: #16 village children (tame, not fleeing) walked off on all 3 build carries; held in Carry pose in the editor only. Likely the 25% tame walk-off in Interactions.Apply
- Checks: compile clean; 1 build (Succeeded). Build log: Dog aggression 0.60 Hunt, "CreatureAudio: voice Growl (52)" at 7.0 m; Dog 0.68 pounced and ate; calm Dog 0.49 at 4.7 m in Play, 0.31 at 5.1 m in Play. Carry: 21.8 kg tame (leash 68 m) child ended Wander, held False. 0 exceptions both runs. No Punchy.exe left
- Shots: PunchyMilestones/animal/a20/watchhunt-800.png (white dog side-on mid-hunt in the meadow, hare out of frame); a20/chase-800.png (dog stalking side-on, HUD on, no hare); a20/chase2-800.png (huntside on a wolf, not reviewed)
- Learned: docs/agents/fauna.md (match species by name); lanes/animal/TESTING.md (-shotonhunt ends the line)
- NEXT: #16 calm test flag on InteractionRequest (or no walk-off for folk children) plus one build; #7 a hare-only meadow run so huntside picks a dog
- Budget: ~62 calls, 1 build

## 2026-11-02 batch A-18: herd calls at the budget; whole wolf mid-gallop; colossus and mantrap back in solo
- DONE: #43 calls within 6 m still play but now take their share of the budget (debt down to 10); calls the biome can't sound no longer spend it (animal-behaviours b549d00)
- DONE: #6 hunt reshot in the open meadow (-255, 27) with -huntside 6; -huntside now counts plants between camera and hunter and takes the clearer side (same commit)
- DONE: #49 client run extended past the reload: after the host quit, the client logged DunesColossus placed (1780, 12.7, -1973) and Mantrap placed 26 again, then 45 s solo
- Checks: compile clean; 1 build (Succeeded, 0 errors). Herd (6 Deer + 4 Bison, 120 s): 73 calls in 2 min = 36.5/min (A-15: 94, ~47/min); 11 windows after the first 66 in 110 s = 36.0/min, the extra one is the 2-call burst saved before the window. huntside: wolf 9.9 m/s, stage 1, 0 plants in the way. Host/client: 0 exceptions both. No Punchy.exe of mine left (one world-lane Builds/Dev player running, left alone)
- Shots: PunchyMilestones/animal/a18/hunt-800.png (whole wolf side-on in open grass, mid-bound with all four legs gathered under it); a18/herd-800.png (herd test view, not reviewed)
- Learned: lanes/animal/TESTING.md (hunt spot and the plant count)
- NEXT: #7/#12 wait on Zach; #43 strict 36.0 needs CallBurst lowered or a 0.59 rate if Zach wants it exact
- Budget: ~45 calls, 1 build

## 2026-11-02 batch A-17: no client NRE when the host quits; side-on hunt reshoot
- DONE: #49 (new) client falling back to solo no longer throws in TerrainSource.HasBiomes: DunesColossus and Mantrap re-fetch TerrainSource when the default world changes and rebuild after the scene reload (animal-behaviours 17d4b40)
- DONE: #6 hunt reshot side-on 1 s into the sprint in clear weather (new -huntside flag, same commit)
- Checks: compile clean; 1 build (Succeeded, 0 errors); host/client run, host quits at 30 s: client 0 exceptions, host 0 (A-16: 1 NRE). Colossus re-placement in the solo game not seen (client list ended at the reload). huntside log: Wolf 7.1 m/s, stage 1, 1.0 s after sprint start. No Punchy.exe of mine left.
- Shots: PunchyMilestones/animal/a17/hunt-800.png (7 m: wolf mid-stride, partly behind a shrub; clear sky); a17/retry/hunt-800.png (6 m: legs spread fore/aft, a bound reads, back half behind the shrub)
- Learned: docs/agents/multiplayer.md (cached TerrainSource and AfterSceneLoad after a world swap)
- NEXT: #6 if Zach wants the full body: another hunt spot without the shrub; #7/#12 wait on Zach

## 2026-11-02 batch A-16: songbird windows in; client sees a remote roll; Growl blocked by dog data
- DONE: #12 five Wander/flying windows of 25 s+ with the player standing still (16 Songbirds): 24.1, 19.8, 10.5, 16.0, 46.4 deg/s (38/30/35/27/26 s), weighted ~22.5 (A-14 ~23.5)
- DONE: #48 client logs the remote roll (Roll 0.03, shot at 0.40) and sees the host upside down mid-somersault (new -remoteroll test flag, animal-behaviours 995ca68)
- DONE (why not): #7 Growl can't fire: Dog.asset Hunts [], FleeDistance 0, no species hunts dogs; 3 dogs by 6 rabbits 60 s = 0 voices. Design call needed
- NOT DONE: #6 hunt.png caught the wolf at sprint start (1.2 m/s), tiny and far, rain on the first line; gallop not readable
- Checks: compile clean; 1 build; 0 exceptions solo/host; 1 NRE on client after host quit (TerrainSource.HasBiomes in fall back to solo); host roll 0.53 s 4.1 m
- Shots: PunchyMilestones/animal/a16/client/remoteroll-800.png (host's Human mid-somersault seen by the client); a16/hunt-800.png (wolf too far, not usable)
- Learned: lanes/animal/TESTING.md (remote roll recipe), STATE.md test notes (16 birds for 25 s windows; -shotonhunt file name)
- NEXT: #6 reframe (closer, ~1 s into the sprint); #7 decide whether dogs chase rabbits; client NRE on host quit (file it?)
- Budget: ~62 calls, 1 build (reshoot of #12 with 16 birds in the soft extra)

## 2026-11-02 batch A-15: lizard seen mid-climb on the barn wall; Hiss heard; herd calls counted
- DONE: #33 mid-climb shot from 9 m out in front of the barn face shows the Lizard on the wall (animal-behaviours 99b6fa7).
- DONE: #43 94 played calls in 120 s near 6 Deer + 4 Bison = ~47/min (budget 36/min; calls within 6 m skip it, ambient included).
- NOT DONE: #12 one try kept (spots within 60 deg, steeper close near the spot): 38.3 (18 s), 0.0 (36 s), 13.2 (5 s), 12.6 (21 s) deg/s, weighted ~12.7 vs ~23.5; only one 25 s+ window because the birds rest or flee the autowalker.
- NOT DONE: #7 Hiss(49) played twice (fleeing cats, focus ~3 m); Growl(52) never: the dogs never entered Hunt or Flee near the wolf in 70 s.
- Checks: compile clean; 2 builds; run 1 (4 shots) and wallclimb reshoot quit themselves; no Punchy.exe left. Wall search: settlement 3 tallest 1.7 m, moved to settlement 6 (4.9 m); UP at 1.2 s, shot at 2.5 s.
- Shots: PunchyMilestones/animal/a15/wallclimb-800.png (barn corner, Lizard head-up on the face right of the door). song/voices/herd-800.png are log runs, not checked.
- Learned: lanes/animal/TESTING.md (wall shot framing), STATE.md.
- NEXT: #12 a long songbird run with the player standing still to get three 25 s windows; #7 a dog-by-rabbit test for Growl.
- Over budget: about 75 calls (card 60), 2 builds; second build for the #33 reshoot (camera square to the face).

## 2026-11-02 batch A-14: songbird turn cause named; lizard tops out a 4.9 m wall
- DONE: none fully. Code in animal-behaviours 753e5de.
- NOT DONE: #12 cause named (takeoff turns to spots behind the bird, 150-280 deg/s, and wheeling near the spot, 140-800 deg/s); tree picks now ahead-only, but Wander/flying is still 27.2 (40 s), 28.4 (18 s), 15.3 (16 s) deg/s.
- NOT DONE: #33 lizard climbs a 4.9 m flat-topped barn wall (settlement 6): UP 1.0 s, TOP-OUT 6.6 s, STEP-DOWN 10.2 s, DOWN 10.8 s. The mid-climb shot has the camera against the barn with the face faded out; no lizard visible.
- Checks: compile clean, one build, player quit itself (3 shots in 188 s), no Punchy.exe left. Wall search logged settlement 3 tallest flat top 1.7 m, moved 616 m to settlement 6. Songbird windows from the player log.
- Shots: PunchyMilestones/animal/a14/wallclimb-800.png (village from beside a barn, barn face faded, no lizard). song.png (songbird run, not checked).
- Learned: lanes/animal/STATE.md.
- NEXT: #33 frame the climb from the `to watch` camlook 8-10 m back; #12 trace ground-spot bearings over 90 deg at takeoff and let birds descend steeper near the spot.
- Over budget: about 110 calls (card 60), 1 build; #12 fix iterations in the editor (3 tries, 2 reverted).

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

## A-11b (2026-11-02): ocotillo mimic polish, #17
- animal-behaviours 8a3349e. Mimic now 10 arms (5 pairs), splay 18-28 deg (mean 23) vs real recipe 9-18 stems, 4-28 deg (mean 16), 2.5-5 m.
- Startle 11.5 -> 5.2 m (FleeDistance 2.4); fold 0.55 s; run max 15.8 -> 8.99 m/s (RunSpeed 3); settle (Rest, curl 0 to 0.95) 4.97 s.
- Shots in PunchyMilestones/animal/a11b: rest (mimic and real side by side, full height, camera far), run (mid-flee), settle.
- NEXT: Zach's look; tune FleeDistance 2.0 / RunSpeed 3.2 if wanted; closer rest camera.

## A-12 (2026-11-02): mimic, songbird turns, colossus, animal-behaviours 36f6dab (one build)
- #17: FleeDistance 2.0, RunSpeed 3.2. Startle 4.6 m, run 10.13 m/s mid-run, fold 0.55 s. mimic-rest: two ocotillo (mimic and real) fill the frame from 5 m. mimic-run: mimic on its arm-legs mid-flee, framed on it alone.
- #12: fliers pick new spots within 90 deg of heading. Wander/flying 23.1, 16.4, 43.6 deg/s over three windows: improved but noisy.
- #40: colossus 968 m from the nearest settlement. col300: the giant seen side-on across dunes, no HUD.
- #46: not settled, test spot was not flat (gap 2.3-2.9 m). #33: not done.
- NEXT: flat spot for #46; damp takeoff turn for #12; `-place wall:<Species>` for #33.

## A-13 (2026-11-02): wall test, flier turns, flat-spot fear rows, animal-behaviours dedc3db (one build)
- #33: `-place wall:Lizard`; up 1.1 s, top-out 2.0 s, step-down 10.3 s, down 10.6 s on a 1.1 m wall; sideways 0.9 m/s. Mid-climb shot missed (camera overridden).
- #12: NOT met. Songbird Wander/flying 0.1, 28.3, 56.7 deg/s (short samples). Yaw-rate limit and takeoff damp added.
- #46: flat spot (-357, -47), spread 0.39 m. Weighted vs flat fled: ground 1,0,0,0 vs 0,0,0,1; 5 m 0 vs 5 once; 15 and 30 m all 0. No gain visible.
- NEXT: #12 per-bird target-jump trace; tall garden wall and a working mid-climb shot for #33.

## A-19 (2026-11-02): personality, dogs hunt hares, carry by weight, animal-behaviours e37a463 (one build)
- #50 met: aggression is worked out from the replicated Individual. Host and client gave identical values for 10 dogs (0.130 to 0.875).
- #7 NOT met: the hunting gate is in place (dogs at 0.6 and up hunt hares, calm dogs never do, and hares ignore calm dogs), but the run had no hares near any dog, so no chase and no Growl.
- #16 partly met: Dog 36.4 kg carried; Human 65.2 kg too heavy. Child 23.6 kg passes the weight check, but the spawned wild child fled.
- NEXT: find where the spawned rabbits go in the meadow run, then a dog-chase plus Growl shot; carry a tame or village child.

## A-21 (2026-11-02): leaping run, child carry, dog chase, animal-behaviours 08da339 (one build)
- #6 met: wolf, dog and cat use a leaping run (GallopStyle.Leap). The gathered and extended phases both read in side-on shots (frozen with -leapphase). Deer and horse stay Rotary (from -gaitlog). Coyote inherits Leap as a Wolf variant.
- #16 met: the walk-off is rolled per attempt, and a settled village child never walks off. The build log shows HumanChild Held in the Carry pose on 2 of 2 carries, and a shot shows the adult holding the child.
- #7 met: a dog sprinting at a jackrabbit 7.7 m away, side-on, both in frame.
- NEXT: Zach's look at the leap poses (front-leg spread, gather hold length).

## A-22 (2026-11-02): render-system cave cost, leap retake, animal-behaviours 7844da3 (one build)
- #23 met on v1: the cause was the cave cull's per-creature managed SampleHeight on the main thread, at 1.6 ms (100 creatures) and 2.1 ms (131). Now a Burst job at 0.03-0.04 ms. System total 1.9 -> 0.33 ms and 2.4 -> 0.44 ms. The before/after cave shots look the same. Not yet confirmed on v2.
- #6 retake: the log reads "4 m side-on", but in the shot the wolf faces the camera, so the gathered phase is not shown side-on.
- NEXT: on v2, `-renderpasslog` with and without `-oldcull`; make -huntside place the camera from the hunter's heading at shot time.

## A-23 (2026-11-02): side-on hunt camera, leap and dog-chase retakes, animal-behaviours 5557c9a (two builds)
- #6 met: -huntside now sits off the hunter's own heading at shot time (the hunter-to-prey line was 157-162 deg off it). Gathered and extended leap phases both shot side-on at 4 m; gait values unchanged.
- #7 met: dog chase retaken side-on at 5 m with the new -huntpair (aims between hunter and prey); dog and hare both clearly visible, 2.1 m apart.
- Second build: back-to-back -shotonhunt lines caught the last line's wolf walking (extend shot 1); fixed by skipping that hunter until its sprint ends, then retaken.
- NEXT: Zach's look at the leap poses (leapgather-800, leapextend2-800) and dogchase-800.

## A-24 (2026-11-02): merge rehearsal and #23 on v2, local merge-test ac8031a (one build)
- Merge rehearsal: merging origin/world-features fb6d7ee into animal-behaviours 5557c9a (local `merge-test`, not pushed) hits one conflict, docs/agents/rendering.md (both sides added a bullet at the end; kept both). The editor compiles with 0 errors.
- #23 confirmed on v2: at the v2 cave stop with 135-145 creatures, CreatureRenderSystem takes 7.1-7.3 ms with `-oldcull` and 0.32-0.35 ms with the Burst cull (cull 7.0 -> 0.2). Hashes hold: v2 127 / 831FF2EE, v1 101 / E128CB1D. 0 exceptions.
- Worktree is back on animal-behaviours, clean (apart from the old untracked Assets/_Recovery).
- NEXT: Zach can close #23; merge-test is ready for the real merge when the supervisor calls it.

## A-25 (2026-11-02): leap poses eased, animal-behaviours a077715 (one build)
- #6 row 1 met: the leaping run's hip is drawn 30 deg back from both extremes (CreatureAnimator.LeapHip). Wolf upper leg from vertical, body frame, before -> after: front gathered -91 -> -61, front extended 93 -> 64, hind gathered 102 -> 72, hind extended -63 -> -33. Timing, spine flex, rotary gallop and bound unchanged.
- #6 row 2 met: side-on 4 m retakes, viewed at 800 px. Gathered (10.7 m/s): feet tucked, no leg near horizontal. Extended (11.6 m/s): airborne, front legs on a forward-down diagonal, hinds back. The first extend shot fired at 6 m/s (part-blended) and was retaken with the same build.
- NEXT: Zach's look at leapgather-800 and leapextend2-800.

## M-01 (2026-11-02): both lanes merged into main, main 0cc3ed7 (one build)
- Row 1: `merge/lanes-to-main` from origin/main 50da96e; `--no-ff` merges of animal-behaviours a077715 (0e99c30) and world-features 73709c8 (6cd3978). Conflicts, docs only: CLAUDE.md (main's "repo is public" commit vs the animal docs/agents split: kept the animal side, which already has the public note; build-release.md has the public-release line), docs/agents/rendering.md (kept both bullets, as rehearsed). Both stayed in their own line endings (CRLF, LF). No conflict markers.
- Row 2: editor recompile on the branch, 0 errors (BatFolkBaking loaded).
- Row 3: build Succeeded; v1 101 / E128CB1D, v2 127 / 831FF2EE, `[batfolk] caves 3, picked 1` on v2; 0 exceptions; both players quit on their own. Logs in PunchyMilestones/animal/m01/.
- Row 4: PR #51 merged with a merge commit; main 0cc3ed7 contains both lane heads (merge-base --is-ancestor). Lane branches kept.
- Row 5: worktree back on animal-behaviours, clean (apart from Assets/_Recovery), editor recompiled with 0 errors. Not done: deleting local `merge-test` (ac8031a); `git branch -D` was blocked by the permission classifier and `-d` refuses (its merge commit isn't on main).
- NEXT: Zach or the supervisor deletes local merge-test (and the merged `merge/lanes-to-main` branches if wanted).

## A-26 (2026-11-02): coyote leap, folk talks
NOT DONE (task 3 shot). animal-behaviours 855a835 pushed.
- #6: coyote inherits Wolf's Leap; build `-gaitlog`: `gait Coyote: Leap`. No code change.
- Folk talks in code: two townsfolk stop, face each other, take turns talking (hand beats) and listening (nods),
  4-10 s, then resume their walk; cooldown 90-200 s per folk, at most 3 talks per town, children may stand by.
  Local fields and Idle stages only (rule 11 kept); host/solo log `FolkChat: Human & Human at (141, -396) in
  Stonewick for 10.2 s` (two in the run). `-chattest` forces, `-chatshot` frames. Synced: would need the partner sent.
- Shot: both talks finished before the `-chatshot` line began; no talks for the next 120 s despite `-chattest`,
  so shots 1-2 show a lone folk (viewed at 800 px). Suspect few residents stay ChatFree once at their day jobs.
- Checks: compile clean, build Succeeded, no exceptions in the player log. v1 101 / E128CB1D could not be read: this
  branch lacks main's `[island]` hash log (main not merged in). Gait values untouched; no placement code touched.
- Shots: D:\Unity\PunchyMilestones\animal\a26\a26-shot0..4-800.png (full size in shots\).
- NEXT: in play mode, count Mode/Task/Leash of Stonewick folk at 30-60 s, widen ChatFree if needed, retake
  with `-chatshot` on the `-place` line. Merge main into animal-behaviours for the hash line.

## A-27 (2026-11-02): merge main, folk keep talking
NOT DONE (task 3 shot; task 2 partly). animal-behaviours c601c66 pushed (merge ed767e0 of main 0cc3ed7).
- Task 1: merged main with a merge commit, no conflicts, 0 compile errors, pushed.
- Task 2: ChatFree now also allows the walk to and the stay at a post, and the walk to a seat. A talk from work
  goes back to the post. The reach is 30 m (they walk up to each other), with a wider grid search. Rarity limits
  unchanged.
  - New `-chatmix` 15 s mix log. It shows no stale roles. The limits are the 90-200 s cooldown and free folk
    30-130 m apart.
  - Build run 1 (114 s): 3, 1 talks per minute. Run 2 (276 s): 1, 1, 0, 0, 1 (gap from 90 to 240 s).
  - Talks now continue past the first 20 s (before: none after 20 s). Two-minute gaps remain.
- Task 3: `-chatshot` framed a pair at the Stonewick well, side-on at 4 m, 0 plants in the way. It is not
  usable: the two stood 0.3-0.5 m apart, overlapping, and no bubble showed. `-nohud` hides bubbles
  (InteractionPrompt.Hidden). Run 2 had the HUD on and still no bubble. Viewed at 800 px.
- Task 4: `[island] generator version 1 (baked); 101 landmarks, hash E128CB1D` in both runs.
- Checks: compile clean, build Succeeded, 0 exceptions, both players quit on their own (no Punchy.exe of mine left).
- Shots: D:\Unity\PunchyMilestones\animal\a27\a27-shot-0..3.png (run 1, -nohud) and a27-run2-shot-0..4.png
  (run 2, HUD on). Full size in full\ and full2\. Logs: a27_log.txt, a27_log2.txt.
- Budget: over (about 100 calls, 2 builds plus one rerun of build 2 without a new build). The soft allowance
  was used for the reach fix and the bubble retry.
- NEXT: fix the talk spacing when the pair is already close (well/seat) and why no bubble shows. Then retake the
  shot without `-nohud`. Ask Zach whether 2-minute gaps are fine or whether the cooldown should drop.

## A-28 (2026-11-02): talk spacing and bubble
DONE (4/4). animal-behaviours 0b06bf3 pushed.
- Task 1: pairs that meet already close now step back to the talk spacing, still facing each other; walking up,
  they slow as the gap closes (the full-speed stop had carried both into each other). Each `FolkChat:` line now
  ends with the distance. Editor run: four talks ended 1.28, 1.28, 1.29, 1.28 m apart, including the Stonewick
  well pair at (133, -418) that stood 0.3-0.5 m apart in A-27.
- Task 2: the bubble was being drawn, but at eye height as a bare "...", so it read as a pair of eyes (that is
  why A-27 saw none). Shown is only for lines said to the player within 5 m, so it is empty for a chatshot
  camera by design. The bubble now sits 0.4 m higher in a pale rounded box. Editor shot shows it.
- Task 3: build run, HUD on, `-chatshot`: two separate folk facing each other at 1.3-1.4 m, bubble over the
  speaker. Viewed at 800 px. Shot 1 (second line) has the bubble touching the tall elder's head top.
- Task 4: `[island] generator version 1 (baked); 101 landmarks, hash E128CB1D`. The no-flag run is v1 on this
  branch (only `-island v2` switches; main's v2 default is not merged here), so one run covers both.
- Checks: compile clean, build Succeeded, 0 exceptions, player quit on its own (no Punchy.exe left).
- Shots: PunchyMilestones animal/a28: build_talk_0_800.png (main), build_talk_1_800.png, editor_talk_800.png
  (full size in build/ and editor/).
- NEXT: optional bubble height per body size; Zach's call on the 2-minute talk gaps (cooldown).

## M-02 (2026-11-02): both lanes merged into main, main 0bd91cb (one build)
DONE (all five rows).
- Row 1: `merge/lanes-to-main` reset to origin/main 0cc3ed7 (the old local branch was already in main); `--no-ff`
  merges of animal-behaviours 0b06bf3 (f8d1263) and world-features afc068c (d0e9204). No conflicts
  (docs/agents/rendering.md auto-merged, still LF). No conflict markers.
- Row 2: editor recompile on the branch, 0 errors (BatFolkMeshes in Assembly-CSharp).
- Row 3: build Succeeded (Builds/Windows). v1 (`-island v1`) 101 / E128CB1D, `[batfolk] caves 0`; v2 (no flag)
  125 / D709D95C, `[batfolk] caves 3, picked 1`. 0 exceptions; both players quit on their own. Logs and shots
  in PunchyMilestones/animal/m02/.
- Row 4: PR #52 merged with a merge commit; main 0bd91cb contains both lane heads (merge-base --is-ancestor).
  Lane branches kept.
- Row 5: worktree back on animal-behaviours, clean (apart from Assets/_Recovery), editor 0 errors. Local
  merge-test deleted (both its parents are in main).
- Note: the editor could not be closed before the branch switches (closing it was refused); the open scene was
  clean, and a refresh plus recompile went through without dialogs.
- NEXT: merge origin/main into animal-behaviours (and world-features) so the lanes carry the merge.

## A-29 + A-29b (2026-11-02): folk jobs and role dialogue (one build)
DONE (all four rows). animal-behaviours de2e8e0, on top of the origin/main merge 70737ed.
- A-29: FolkJobs (job enum, task-to-job map, mood, village post table, bat-folk chamber jobs), `byte Job` in
  CreatureData (local, not a ghost field), village posts and `FolkJobs:` log lines in FaunaDirector, role lines
  in FolkTalkSystem (`FolkLine:` log), DialogueArchive.PickRole with mood tags, dialogue.json role pools (251
  lines), `-watch Human:<Job>`.
- Row 1 (editor, v2): two fixes. `-place` used landmark spots cached from the baked v1 island (now reset when the
  landmark set changes). The bat chamber line was blocked by a static flag set by the v1 blob (now a per-island
  key). Job counts:
  - HumanVillage (2972, -885): Baker 1, Healer 1, Smith 1, Gatekeeper 1, Woodcutter 1, WellKeeper 1, free 6
  - ElfGrove (-2511, -1518): Healer 1, Guard 1, WellKeeper 1, Gatekeeper 1, free 5
  - DwarfHold (-2338, 1995): Baker 1, Healer 1, Guard 1, Smith 1, free 6
  - OgreCamp (-2074, 1266): Baker 1, Gatekeeper 1, Merchant 2, free 5
  - Bat-folk chamber 0 (cave 1) at (3001, 3, -2135): RoostKeeper 3, Tanner 2, Forager 6, PoolFisher 1 (planned)
- Sample lines: WellKeeper (Human, Plain) "Mind the rope, it's fraying."; Baker (Human, Grumpy) "Bread's in the
  oven, mind the heat."; Healer (Human, Cheerful) "Everyone's mending nicely this morning."; WellKeeper (Elf,
  Cheerful) "Deep and cold, the way a well should be."; Smith (Dwarf, Cheerful) "Bring me iron and I'll make it
  useful."; Smith (Dwarf, Cheerful) "Hot work, the forge."; Baker (Ogre, Plain) "First loaves were out before
  dawn."; Baker (Ogre, Plain) "Flour on everything. Always."; build: Healer (Human, Grumpy) "Another scraped knee
  from the woodpile."; Woodcutter (Human, Cheerful) "Nothing like a clean split!"
- Row 2: eol checked (CreatureData stays CRLF), committed de2e8e0, pushed.
- Row 3: build Succeeded. v2 (no flag) 125 / D709D95C; v1 (`-island v1`) 101 / E128CB1D. Both players quit on
  their own. Shots viewed at 800 px: Baker walking to work, Smith at the forge, a talk with the WellKeeper line
  "Everyone comes by the well sooner or later." in the bubble.
- Not seen: Herder, Fisher, Farmer, Trader (no matching anchors in these villages).
- Shots: PunchyMilestones animal/a29: a29-talk-2.png (role line), a29-smith-at-work.png, a29-baker-at-work.png,
  a29-wellkeeper.png, a29-talk-healer-line.png (bubble shows "..."); full size in raw/.
- NEXT: job-specific work poses or props; Job as a ghost field if jobs ever stop following the task.
## A-30 (2026-11-02): cave collision and camera
- Row 1 DONE: causes were that the body radius did not scale with size (0.35 m always), the ground probe was one
  point, there was one wall test at floor+1 m and no ceiling test, and steps were only blocked over 0.6 m. Old max
  penetration on v2: seg 246 (65 deg) 0.46 (size 1), 0.60 (size 1.6); seg 87 (55 deg) 0.79 (size 1.6); seg 9 0.34;
  bat chamber 0.14.
- Row 2 MOSTLY: PlayerMovementSystem.PushOutOfSolids (knee/middle/head spheres scaled to size, 4 passes, steep
  floor slide, ceiling undo); SolidDistance is the landmark hook. New max: seg 246 0.035 / 0.039, seg 9 and the
  chamber 0. Left: seg 87 brief spikes up to 0.20 (6 of ~624 frames), size 0.45 wedged at about 0.05 on seg 246.
  Open ground is untouched (gated on caves).
- Row 3 DONE (no avatar fade): CaveArm sphere trace, ease out, pivot drop under ceilings, near plane 0.1 / 0.05.
  Probe over 10368 views: view-in-rock 589 -> 12, lens-in-rock 239 -> 25.
- Row 4 PARTIAL: one build, succeeded, v2 125 / D709D95C, quit on its own. The camera shots are clean (wall,
  wall2, passage, ceiling: no rock interior). The slope before/after shots show only dark rock (`-camlook`
  framing missed the body), so there is no visual before/after.
- World card: TerrainSource.CaveDistance overstates the distance by up to 1.8x below a passage's axis; make it a
  true lower bound.
- Commits bd26880, f50f7a0 on animal-behaviours, pushed.
- Shots: PunchyMilestones animal/a30: a30-cam-wall.png, a30-cam-wall2.png, a30-cam-passage.png,
  a30-cam-ceiling.png, a30-slope-before.png and a30-slope-after.png (unusable); full size in raw/.
- NEXT: retake the slope shots with `-goto` + `-view`; seg 87 spikes and the small-body wedge; avatar fade; the
  talk-bubble "..." (A-29); v1 hash check.
## A-31 (2026-11-02): crossing fishers, talk bubble, slope shots
- Row 1 DONE: RoadCrossings reads bridges and fords from the settlement bake, with a bank spot for each. v1: 5
  crossings (5 bridges, 0 fords), all 5 with a spot; samples 233.5,-641.1 / 590.5,-1312.0 / -1022.2,1233.2 /
  370.4,3328.2 / 1454.6,2953.8. v2: 3 (3 bridges, 0 fords), all with a spot: -1672.8,2264.9 / -2544.3,884.6 /
  -2993.5,-1448.9. Terrain, anchors and road baking untouched.
- Row 2 DONE: a seeded 1 in 3 roll from the position. v1 gives 3 fishers (583,-1315; -1024,1223; 1449,2953), all
  lone; v2 rolled 0. The editor run and the player run gave the same lists. Job Fisher, task Fish, village home
  if one is within 250 m, else lone and leaves at night; race from the nearest settlement.
- Row 3 DONE on v1: a31-v1-fisher-bridge.png (fisher on the bank with a rod, bridge, road and river in frame).
  v2 has no fisher (none rolled).
- Row 4 PARTIAL: the fix is in (cache only non-null lines; plain-line fallback for jobless talkers). The build log
  shows `FolkLine: Baker (Human, Grumpy): "Flour on everything. Always."`, but neither talk shot shows a bubble.
- Row 5 PARTIAL: the slope shots were retaken with -goto/-view (before with -nopushout), with the body in frame
  but the slope hard to read. v1 logs 101 / E128CB1D; v2 logs 125 / D709D95C.
- Row 6 NOT DONE: out of calls; in NEXT.
- One build, succeeded; the player quit on its own both runs. Commit 6d12e17 on animal-behaviours, pushed.
- Shots: PunchyMilestones animal/a31: a31-v1-fisher-bridge.png, a31-talk-line.png, a31-talk-line2.png,
  a31-slope-before.png, a31-slope-after.png (a31-fisher-bridge*.png are v2 and have no fisher); full size in raw/.
- NEXT: bubble shot (-nohud or timing?), wider slope framing, seg 87 spikes and the small-body wedge, why no fords.

## A-32 (2026-11-02)
- Row 1 DONE: fallback fisher at the best-scored crossing when an island rolls none. v2 log: `fishers 1 (1 lone;
  0 rolled) ... fishers at (-2542,891 bridge best score)`; v1 unchanged (3 rolled, probe). Shot a32-fisher-bridge.png.
- Row 2 DONE: -nohud hid bubbles (InteractionPrompt.Hidden). -chatshot now keeps them. a32-talk-line.png shows
  "Lovely ring to the anvil today." (Smith, Human), viewed at 800 px.
- Row 3 NOT DONE: pulled-back pairs (a32-slope-before/after, -wide) taken, but the A-31 spot is a cave pool, and
  before and after look the same. NEXT gives mid-slope spots.
- Row 4 DONE (probe): seg 246 size 0.45 maxPen 0.051 -> 0.087 at frame 0 only, mean 0.025 -> 0.001, >5cm 312 -> 3,
  progress 0.23 -> 0.74. seg 87 size 1 0.202 -> 0.094; all remaining spikes are at frames 0-2 (start in rock).
  Camera viewInRock 12 -> 13, lensInRock 25 unchanged.
- Row 5 DONE (shot only): bodies fade within ~0.6 m of the camera; a32-close-cam.png from inside the head shows a
  clear view. No before shot.
- Hashes: v2 125 / D709D95C (player log). v1 not re-run in the player (no landmark code touched).
- One build, succeeded; the player quit on its own. Commit cbdf5d1 on animal-behaviours, pushed.
- Shots: PunchyMilestones animal/a32 (800 px), full size in raw/.

## A-33 (2026-11-02)
- Row 1 PARTLY DONE: job -> pose/prop:
  baker = kneading at a table, dough, white cap and apron, loaf (a33-baker);
  healer = bent over, bowl, satchel and strap (a33-healer);
  smith = hammer at the anvil, plus tongs and hot bar (a33-dwarf-smith; the human smith was idle);
  gatekeeper/guard = spear upright in the right hand, slow look-around (a33-gatekeeper, a33-dwarf-guard);
  well keeper = rope and bucket, hand-over-hand haul (a33-wellkeeper);
  woodcutter = axe swing, block, log, split piece (a33-woodcutter, hidden behind a log pile);
  merchant = gesture with a jar or fruit (not framed: -watch Ogre:* missed at OgreCamp);
  bat-folk roost keeper/tanner/forager = none (no walking bat-folk species yet).
  Derived from job/task only, no new network fields.
- Row 2 NOT DONE: seg 87 (both sides) and seg 9 pairs with -nopushout are identical. The push-out only acts on a
  moving step, so a standing player never differs. It needs a walk-down flag (see NEXT in STATE).
- Row 3 PARTLY DONE: HumanVillage by day, 4 folk at work in different jobs (baker, healer, gatekeeper, well
  keeper) plus a33-humanvillage-wide. Second village DwarfHold: guard and smith. OgreCamp shots missed.
- Hashes: v2 125 / D709D95C (player log), 0 exceptions, the player quit on its own. v1 not re-run (render code only).
- One build, succeeded. Commit ab3c458 on animal-behaviours, pushed.
- Shots: PunchyMilestones animal/a33 (800 px), full size in raw/.

## A-34 (2026-11-02)
- Row 1 NOT DONE (flag done, difference not obvious): `-walk x,y,z` walks the player toward a point and logs height
  over floor and metres pushed (PushedTotal). seg 87: the -goto start floors at y 47.4 (not 54.4) and the walk goes
  off a drop, so the player is airborne 3-6 m over the floor. Before (-nopushout): pushed 0 m. After: 0.9 m in the
  first 0.25 s, 1.6 m in all, then 0. The 0.5 s pair (a34-slope87-*-early) shows the body beside the wall in both,
  slightly lower and nearer in "after"; the 1.0 s pair lost the body off frame. No visible clipping difference.
- Row 2 PARTLY DONE: -watch fixed. "Ogre:Merchant" found nothing because OgreCamp's merchants are Goblins; "119 m
  below" was TargetHeight 0 at work. Added `*` species, a once-only warning naming who holds the job, and a "no
  height" readout. Shots: goblin merchant at the stall (gesture does not read clearly, side shot blocked by a post),
  ogre gatekeeper at work with spear, ogre baker walking to its post (Work stage 0). New `-untilwork S`.
- Row 3 DONE: woodcutter with axe over head, log pile beside not in front (a34-woodcutter-a); human smith hammering
  at the anvil with sparks (a34-smith-side).
- Hashes: v2 125 / D709D95C (player log), 0 exceptions. v1 not re-run (debug/movement-probe change only).
- One build, succeeded. Commit a5c8e9e on animal-behaviours, pushed. Shots: PunchyMilestones animal/a34 (800 px), raw/.

## M-03 (2026-11-02): both lanes merged into main, main 41a9259 (one build)
DONE (all five rows).
- Row 1: `merge/lanes-to-main-3` from origin/main 0bd91cb; `--no-ff` merges of animal-behaviours a5c8e9e (6abd705)
  and world-features fd91300 (d4b6c48). No conflicts. PlayerMovementSystem.cs auto-merged (still CRLF) and has both
  the world lane's `KeepOutOfTower` call and the animal lane's `PushOutOfSolids` / `PushedTotal`. No conflict markers.
- Row 2: editor recompile on the branch, 0 errors.
- Row 3: build Succeeded (Builds/Windows; the only running Punchy.exe was the world lane's Builds/Dev W-34 player).
  v2 126 / 053C63D5, `[batfolk] caves 3, picked 1`, fishers 1; v1 (`-island v1`) 102 / 51EA6463, `[batfolk] caves 0`,
  fishers 3. Match world STATE. Cave fingerprints are not printed in player logs (WorldProbe lives only in the world
  tree), so not checked. 0 exceptions; both players quit on their own. Logs in PunchyMilestones/animal/m03/.
- Row 4: PR #53 merged with a merge commit; main 41a9259 contains a5c8e9e and fd91300. world-features gained W-34
  (0bdc707) after the fetch, so that commit is not in main yet. Lane branches kept.
- Row 5: worktree back on animal-behaviours a5c8e9e, clean (apart from Assets/_Recovery), editor 0 errors. Local
  merge/lanes-to-main-3 deleted (in main; remote branch kept).
- NEXT: merge origin/main into animal-behaviours so the lane carries the merge; W-34 goes in with the next merge.

## A-35 (2026-11-02): PAUSED for Zach's demo after the build
- Row 1 DONE: origin/main 41a9259 merged `--no-ff` into animal-behaviours (bf170f2), clean, no conflicts; KeepOutOfTower
  kept; editor recompile 0 errors. Pushed. Hashes after the merge not re-read (player not run).
- Row 2 NOT DONE (prepared): editor probe found a continuous 55 deg cave slope (seg 245, 2.5 m, no drop, steps <= 0.59 m).
  Sim: without push-out the body walks up 0.26 m into rock; with it, it slides back (pushed 12.1 m, ends 4.4 m short).
  Shot list Temp/a35_shots.txt ready; not shot.
- Row 3 NOT DONE (code in): merchant gesture made bigger and held longer (4c03cde, pushed); shots not taken.
- Build: 1 (Succeeded, Builds/Windows, from 4c03cde). Player runs: 0. Calls ~37.
- NEXT: run Temp/a35_shots.txt once with -quit, view pairs, read -walk logs and hashes.
## A-35 (2026-11-02): finished (relaunch)
- Row 1 DONE (bf170f2). Player log after the merge: v2 126 landmarks / 053C63D5, v1 (`-island v1`) 102 / 51EA6463. 0 exceptions.
- Row 2 DONE: seg 245 slope (55 deg, 2.5 m, no drop). -nopushout: walks up to 0.2 m from the end, (-1448.4, 85.5, 240.9),
  0.0 m over the floor, pushed 0 m. Push-out on: slides back down to (-1446.4, 79.6, 237.3), 4.3 m from the end, 0.0 m over
  the floor (never airborne), pushed 0.4 m at 0.25 s, 10.3 m by 1 s, 16.7 m by 2 s. Difference obvious: before shows the body at
  the top; the fixed -camrel after shot loses the body (it slid out of frame), so a35b-after-down (camera aimed down the slope)
  and a35b-after-follow show it at the bottom. Note: push-out fights the player on a real 55 deg cave floor.
- Row 3 DONE: merchant front-on with `-watchfrom 1.6,2.5` (+2.5 is the front) once at the stall (`-untilwork 60 -wait 8`): hand
  raised, beckoning, reads. The original -wait 2/3.3 shots caught it still walking to the stall. Ogre baker at the fire, reads.
- Build: 0 this relaunch (A-35's 4c03cde build was current). Player runs: 4 (main list, v1 hash, slope reshoot, merchant
  reshoot), all quit on their own. Calls ~23 this relaunch.
- Shots: PunchyMilestones/animal/a35 (raw + 800).
- NEXT: A-36 warp keys. Follow-up for the supervisor: push-out drives players back down steep cave slopes (seg 245).

## A-36 warp hotkeys (2026-11-02), animal lane

| Row | Status | Notes |
|---|---|---|
| 1 F5-F8 warp keys | DONE | F5 biome, F6 settlement, F7 cave, F8 landmark, Shift back; F5-F8 free (F4 taken by input actions, logged). Nearest first, then cycle. Lands on dry ground like N/-goto. |
| 2 3 s readout + help | DONE | Top-centre banner "F7 cave: Cave #36 (Mountains) 1/37 (-1479, 2106)"; help line row "warp: F5 biome · F6 settlement · F7 cave · F8 landmark (Shift+key back)". |
| 3 Shots | DONE | `-warp biome|town|cave|landmark N` flag; five v2 shots + v1 town; viewed at 800 px. |

Checks
- Log key map + counts: v2 biomes 35, settlements 6, caves 37, landmarks 126; v1 biomes 45, settlements 12, caves 17, landmarks 102.
- Hashes unchanged: v2 126 / 053C63D5, v1 102 / 51EA6463. Build Succeeded 0 errors. 0 exceptions; players quit on their own.
- Rule 11: no network fields; moves the local player only. No world-lane files edited (reads TerrainSource blobs).
- Gap: SettlementData has no name, so towns are named by landmark type + index.
- Framing: biome 1 (Mountains, on a steep slope) has the follow camera inside the hill; v2 town 3 (DwarfMiningCamp) is under canopy. Readout reads in all.

Shots: PunchyMilestones/animal/a36/800: a36-warp-biome, a36-warp-town, a36-warp-cave, a36-warp-landmark, a36-help, a36v1-warp-town (best: cave and v1 town).

Commit: animal-behaviours f82be16.

## A-37 cave slope push-out + warp camera (2026-11-02), animal lane

| Row | Status | Notes |
|---|---|---|
| 1 slope push-out | DONE | Cause: the steep-floor test read one noisy SDF normal at the foot (32-79 deg on lumpy seg 245) and slid the body back for every reading over 50 deg. The knee sphere was pushed off the floor it stood on, and the mid/head spheres pushed back at a 70 deg ledge. Fix: rise over a 2 m span > 60 deg; the knee skips the floor while standing; no push back along a walkable climb. |
| 2 warp camera | PARTLY | Open-ground line-of-sight pull-in (ThirdPersonCamera), and the warp brings the camera in to 5 m at pitch 8. Biome 1 Mountains: player clear. Town 3 DwarfMiningCamp: player visible, but the camera sits in a tree crown with leaves at the frame edges (trees are not in TerrainSource). |
| 3 shots | DONE | Viewed at 800 px. |

Sim numbers (editor, real PushOutOfSolids, 4 m/s, before -> after):
- seg 245 up: progress -1.98 -> 2.26 of 2.43 m; pushed 20.9 -> 0.47 m (15.35 m by 2 s before).
- seg 245 down: pushed 2.88 m in 0.23 s -> 0.20 m over the full walk; progress 2.27 m (full).
- size 0.45 up: progress 2.26 m, pushed 0.25 m.
- flat seg 0 (4 m): pushed 0.03 m, progress 3.80 m, the same as push-out off.
- Clipping: maxPen (mid/head) 0.10 m, against 0.18 m with push-out off.

Checks:
- Build Succeeded (1 build).
- v2 126 / 053C63D5 in both player logs; v1 not run (no world code touched).
- 0 exceptions.
- No new network fields; the KeepOutOfTower line stays.
- Commit d17f6a7, pushed to animal-behaviours.

Deviations:
- Two shot-list runs. In run 1 the warps came after the slope -goto, so they picked different targets (warps sort nearest the player). Run 2 was only load + the two warps.
- Port 7800 reached the WORLD editor (Punchy-world). I worked in the animal editor by PID (it answers on 7801). Before I found this, a Refresh/compile request and Enter/ExitPlaymode calls may have reached the world editor.

Shots (PunchyMilestones/animal/a37/800):
- a37-slope245-up, a37-slope245-up-late
- a37-warp-biome-retake, a37-warp-town-retake
- From run 1: a37-warp-biome (SparklingForest, player clear under canopy) and a37-warp-town (OgreCamp).

NEXT: warp camera vs tree crowns (flora along the camera line, or skip targets under canopy).

## A-38 warp camera vs trees + climb regression (2026-11-02), animal lane

| # | Row | Result |
|---|---|---|
| 1 | Warp camera vs trees | DONE: no leaves in the DwarfMiningCamp retake; the camera turned 120 deg |
| 2 | Climb rule regression | DONE (log below): no tunnelling; found a slide-and-jitter against the slope wall |
| 3 | Shots | DONE: the town 3 retake, plus F5-F8 from the spawn; all viewed at 800 px |

Row 1 (DebugControls.ClearWarpView): after a warp, the camera tries the landing heading, then 30 deg steps either way.
It takes the first side whose head->lens line misses tree crowns and trunks. Plants come from FloraPlacement.Place
(read only; FloraGenerator not touched). The first build tested the plain 8 deg line, and town 3 still had leaves:
on the uphill side, ThirdPersonCamera lifts the lens over the ground and into the crowns. The fix build tests the
lifted lens line, with a 1.2 m margin near the lens.

Row 2 (sim in the editor's Play mode, real PushOutOfSolids, 4 m/s; sink = maxPen at mid/head):
- Seg 245 slope, sideways into the wall: sink 0.03 and 0.01 m with push-out (0.15 and 4.75 m without). Pushed
  18.5 and 13.5 m in 3 s: the body jitters and ends ~1.4 m down, at the slope's foot.
- Slope, diagonally up into the wall: sink 0.14 and 0.07 m (0.31 and 0.34 m without). Pushed 1.2 and 20.8 m.
- Ledge, from the top of seg 245 downhill and past its foot: sink 0.00, pushed 0.20 m, fall 1.38 m.
- Ledge, cave seg 32 (a 1.06 m drop): sink 0.00, pushed 0.53 m, fall 3.0 m.
- Off the A-37 target: one diagonal sink is 0.14 m (over 0.10). The wall jitter and the downhill slide are new
  findings, not fixed (out of scope).

Checks:
- Compile clean. 2 builds Succeeded: the card's build, plus 1 fix build under the soft budget.
- v2 126 / 053C63D5 in the player log. v1 not run (no world code touched).
- 0 exceptions. Both runs quit on their own. The only other Punchy.exe was the world lane's Builds/Dev player.
- No new network fields; the KeepOutOfTower line stays.
- Line endings: DebugControls is lf, kept lf.
- Commit 90e9211, pushed to animal-behaviours.
- Editor: all calls went to the animal editor by --pid. Nothing was sent to Punchy-world.

Shots (PunchyMilestones/animal/a38/800; full size in raw/):
- a38-warp-town3-retake (DwarfMiningCamp: no leaves, player in view)
- a38-warp-biome (Mountains), a38-warp-town (OgreCamp), a38-warp-cave (Cave #34), a38-warp-landmark (CrystalGrotto)
- a38-load

NEXT: on the seg 245 slope, walking sideways into the wall jitters and slides the body down (the wall push has a
downhill part).

## A-39 (2026-11-02): slope + wall push-out

Commit e75918e on animal-behaviours (PlayerMovementSystem.cs only). 1 build, ~70 calls.
- Row 1, PARTLY DONE. On a walkable slope the downhill part of a wall push is dropped, and a non-climbing body more
  than 0.08 m into the wall is held at its last position. The steep slide-back now applies only where the start
  position is steep, which fixed the side-1 jitter. Sim (Temp/a39_sim.cs, editor Play mode, real PushOutOfSolids),
  before -> after:

| Walk | Progress m | Pushed m | Sink mid/head | Slide drop/downhill | Travel |
|---|---|---|---|---|---|
| side -1 | 0.63 -> 3.46 | 18.51 -> 16.26 | 0.03 -> 0.03 | 0.73/1.16 -> 0/0 | 6.36 |
| side 1 | 1.53 -> 1.58 | 13.52 -> 14.45 | 0.01 -> 0.03 | 1.10/0.71 -> 0/0 | 1.78 (frames 100-101 hold still) |
| diag -1 | 3.85 -> 3.80 | 1.22 -> 1.50 | 0.14 -> **0.19** | 0/0 -> 0/0 | 6.96 |
| diag 1 | 1.51 -> 1.79 | 20.81 -> 19.77 | 0.07 -> 0.06 | 0/0 -> 0/0 | 2.34 |

  "Pushed" stays high because each walk step into the wall is cancelled (~0.067 m/frame); travel is the real path.
  Side -1 now climbs along a rising floor (the run without push-out ends at the same place). Diag sink is not met:
  diag -1 is 0.19, while climbing. Holding the body while climbing past 0.10 m got diag -1 to 0.08 but stopped
  seg 245 up at 0.86 m, so it was reverted.
- Row 2, DONE except for one sink regression:

| Walk | Progress m | Pushed m | Sink mid/head | Max air | Slide drop/downhill |
|---|---|---|---|---|---|
| seg 245 up | 2.26 -> 2.25 | 0.48 -> 0.46 | 0.10 -> **0.14** | 0 | 0/0 |
| seg 245 up, size 0.45 | 2.26 -> 2.26 | 0.24 -> 0.81 | 0.07 -> 0.10 | 0 | 0/0 |
| seg 245 down | 2.27 -> 2.27 | 0.20 -> 0.20 | 0 -> 0 | 1.22 | 2.08/2.27 |
| flat seg 0 | 3.80 -> 3.80 | 0.03 -> 0.03 | 0 -> 0 | 0 | 0.14/0 |
| ledge seg 245 | 4.27 -> 4.27 | 0.20 -> 0.20 | 0 -> 0 | 1.38 | 4.66/4.27 |
| ledge seg 32 | 3.85 -> 3.85 | 0.53 -> 0.53 | 0 -> 0 | 3.01 | 2.02/3.13 |

- Row 3, NOT DONE as asked. The before/after pair is taken and viewed at 800 px
  (PunchyMilestones/animal/a39/a39-side-wall-{before,after}-800.png), but the A-38 slope `-camrel` frame faces rock
  on the sideways walk, so no body is visible in either shot. A retake can only give an after shot.
- Checks: player log v2 126 landmarks, hash 053C63D5; 0 exceptions; both runs quit on their own; no Punchy.exe left.
  No network fields. KeepOutOfTower untouched. CRLF kept.
NEXT: sink while climbing near a wall (diag -1 0.19, seg 245 up 0.14); retake the sideways shot with a side frame
found in the editor first.

## A-40 (2026-11-02): climbing sink near a wall + side-on shot

Commit b2b6a70 on animal-behaviours (PlayerMovementSystem.cs only). 1 build, ~60 calls.
- Row 1, DONE. While climbing, a chest or head over 0.10 m into rock is pushed sideways across the climb (at most
  2x radius). With no sideways way out and no rise in the floor ahead, the body holds; otherwise it takes the largest
  part of the step that stays out. A push back down the climb from a clear spot holds instead: without that, diag -1
  shivered 0.15 m in a three-frame cycle at the corner (travel 24 m in 3 s). Sim (Temp/a40_sim.cs, editor Play mode,
  real PushOutOfSolids), A-39 -> A-40:

| Walk | Progress m | Pushed m | Sink mid/head | Slide drop/downhill | Travel |
|---|---|---|---|---|---|
| side -1 | 3.46 -> 3.46 | 16.26 -> 16.26 | 0.03 -> 0.03 | 0/0 -> 0/0 | 6.36 -> 6.36 |
| side 1 | 1.58 -> 1.58 | 14.45 -> 14.45 | 0.03 -> 0.03 | 0/0 -> 0/0 | 1.78 -> 1.78 |
| diag -1 | 3.80 -> 0.76 | 1.50 -> 19.85 | 0.19 -> **0.10** | 0/0 -> 0/0 | 6.96 -> 2.07 |
| diag 1 | 1.79 -> 1.79 | 19.77 -> 19.77 | 0.06 -> 0.06 | 0/0 -> 0/0 | 2.34 -> 2.34 |

| Walk | Progress m | Pushed m | Sink mid/head | Max air | Slide drop/downhill | Travel |
|---|---|---|---|---|---|---|
| seg 245 up | 2.25 -> 2.25 | 0.46 -> 0.79 | 0.14 -> **0.10** | 0 | 0/0 | 4.31 |
| seg 245 up, size 0.45 | 2.26 -> 2.25 | 0.81 -> 1.41 | 0.10 -> 0.08 | 0 | 0/0 | 4.41 |
| seg 245 down | 2.27 -> 2.27 | 0.20 -> 0.20 | 0 -> 0 | 1.22 | 2.08/2.27 | 3.35 |
| flat seg 0 | 3.80 -> 3.80 | 0.03 -> 0.03 | 0 -> 0 | 0 | 0.14/0 | 4.01 |
| ledge seg 245 | 4.27 -> 4.27 | 0.20 -> 0.20 | 0 -> 0 | 1.38 | 4.66/4.27 | 6.64 |
| ledge seg 32 | 3.85 -> 3.85 | 0.53 -> 0.53 | 0 -> 0 | 3.01 | 2.02/3.13 | 5.88 |

  Trade-off: diag -1 now stops at the corner (0.76 m) instead of riding 0.19 m into it along the wall. Tried and
  dropped: holding at every deep step (seg 245 stops at 0.94-0.99 m), stepping up onto the floor ahead, sliding
  along the wall, pushing along the normal (all stop the climb or don't help).
- Row 2, DONE. Side frame found in the editor first (Temp/PlayArgs.txt shot list, body visible), then the after shot
  in the build, viewed at 800 px: body side-on at the wall
  (PunchyMilestones/animal/a40/a40-side-wall-800.png). Frame: `-camlook -1446.2,85.8,237.7,-1446.6,84.4,240.2`.
- CaveDistanceBound (W-33): would not change this fix. The sink comes from where the body is allowed to step, not
  from SDF error, and at ~5x cost it is too dear for 3 spheres x 4 iterations per frame. It could be useful in the
  sim to check that the 0.10 m numbers are not under-read by the estimate.
- Checks: player log v2 126 landmarks, hash 053C63D5; 0 exceptions; run quit on its own. No network fields.
  KeepOutOfTower untouched. CRLF kept.
NEXT: none required. If diag -1 feels sticky at the corner in play, try a wall slide that keeps the 0.10 m limit.

## A-41 (2026-11-02): humanoid run elbows and leg back-swing
Run pose lives in animal-lane files: limb keys Assets/Game/Fauna/Limbs/HumanArm.asset, HumanLeg.asset, played by
CreatureAnimator.cs. Local commit e3b8521 on animal-behaviours, not pushed (Rule 11: shots first).
Blocked: Zach's Punchy.exe from Builds/Windows (PID 34888, started 20:47) ran from 20:59 to past 21:31. Not killed.
No before shot, no build, no player run.
- Row 1, code DONE (not seen in a build). Elbow bend (deg from straight) over a run cycle, HumanArm (Human Elf Dwarf
  Ogre Hermit Goblin Ghost all share it):

| Case | armSwing 0.55 | 1.00 | 1.45 |
|---|---|---|---|
| run | 42-48 -> 85-95 | 70-80 -> 85-95 | 98-112 -> 85-95 |
| sprint | 86-94 -> 85-95 | 149 (clamp) -> 85-95 | 149 (clamp) -> 85-95 |

  Run keys 80/70 -> 95/85; CreatureAnimator now holds an upright runner's elbow at the keys, so the per-folk arm
  swing trait and the sprint pump scale the shoulder only. The sprint fold to 149 was the "severe bend".
- Row 2, code DONE (not seen in a build). HumanLeg RunPush hip 48 -> 32, RunReach -54 -> -60 (keeps most stride).

| HumanLeg | Max hip extension | Stride m/cycle | Foot slide over stance |
|---|---|---|---|
| run | 48 -> 32 | 2.648 -> 2.423 | 14.2 -> 11.8 cm |
| sprint | 65 -> 43 | 3.181 -> 2.949 | 33.3 -> 28.2 cm |

  Stride is measured from the keys (MeasureGait) so cadence rises ~9% and feet stay matched. Slide = the probe's
  estimate of planted-foot travel from the non-linear sweep, not new sliding. GoblinLeg (15 deg) untouched.
- Row 3, PARTLY. Walk keys and walk elbow untouched (hold only weighs in with run). Non-humanoids: the hold needs
  Upright + Arm; leg edit is HumanLeg only. Editor Play run: Human runs, no errors, compiles clean. Not seen in a build.
- Row 4, NOT DONE. Frame picked in the editor (after code): PunchyMilestones/animal/a41/editor-after-run-2.0.png and
  -2.2.png (portrait editor aspect, not the 16:9 pair). Player list ready: Temp/a41_shots.txt.
- Checks: CRLF kept on CreatureAnimator.cs (verified 1466/1466), assets LF. No network fields; no world code (hash
  unchanged by construction, not log-checked). PlayArgs restored.
NEXT: when no Punchy.exe runs, before shots with Temp/a41_shots.txt on the current exe, ONE build, after shots, push.
A-41 finish (after Zach's player exited): before shots on the old exe, ONE build, after shots on the new exe.
Both logs: v2 ready, 126 landmarks, hash 053C63D5. e3b8521 pushed. Row 4 DONE: pair (800 px, side-on run, wait 2.0)
PunchyMilestones/animal/a41/a41-run-pair-800.png (left before: forearm folded, trailing leg stretched far back; right
after: elbow near a right angle, back leg shorter); full-size before-run-*.png / after-run-*.png beside it.
Row 3: walk and animals not re-shot; the after runner reads a little crouched at mid-stance.

## M-04 (2026-11-02): both lanes merged into main, main 5e98141 (one build)
DONE (all five rows). The first pass stopped after row 1 on a C# conflict; the supervisor approved keeping both sides.
- Row 1: `merge/lanes-to-main-4` from origin/main 41a9259. `--no-ff` merge of animal-behaviours e3b8521 clean (6a74ec9).
  `--no-ff` merge of world-features f137084 (33b1f20, head unchanged after a re-fetch): one conflict in
  Assets/Game/Scripts/Debugging/DebugControls.cs (launch flags). Kept both, in order -meteors, `-nopushout` line,
  W-42 `-redstar` block, -nohud. The file is LF on main and both lanes (not CRLF), so it stays LF (i/lf w/lf).
  No conflict markers left.
- Row 2: editor 26472 (dataPath D:/Unity/Punchy/Assets), recompile 0 errors; Archipelago (world) loaded.
- Row 3: no Punchy.exe running. Build Succeeded, 0 errors (Builds/Windows, 2m10s). v2 126 / 053C63D5, batfolk caves 3
  picked 1, fishers 1; v1 102 / 51EA6463, caves 0, fishers 3. Match world STATE. 0 exceptions; both quit on their own.
  Logs in PunchyMilestones/animal/m04/.
- Row 4: PR #65 merged with a merge commit; main 5e98141 contains e3b8521 and f137084. Lane branches kept.
- Row 5: back on animal-behaviours e3b8521, clean (apart from Assets/_Recovery), editor 0 errors. Local merge branch
  deleted (remote kept). No release, no tag, no issues closed.
- Calls ~45, builds 1.
- NEXT: merge origin/main into animal-behaviours so the lane carries the merge.

## A-42b (2026-11-02): skunk gas, lying on side slopes
Commit 89270af on animal-behaviours (pushed). ONE build to Builds/Windows. Hash v2 126 / 053C63D5 unchanged.
1. Skunk gas: PARTIAL. SkunkGas.cs: colour (0.78,0.92,0.2,1), 60-puff burst, trail 50/s, start size 0.35-0.6,
   life 2.2-3.4 s, emitted 0.12 m above the rump, alpha holds to mid-life. Editor close-up (viewed) shows a faint
   green haze on Puffs.mat; the default particle material shows clear green blobs, so the soft shader (dim light,
   0.3 m depth fade, no properties) is what eats it. Build: puffs fire (peak 102 particles, ~0.01 ms avg,
   2.7 ms max) but no cloud is visible in the skunk shots (small skunk on snow). Next: its own puff material.
2. Side slopes: DONE. Probe a42b: below -0.05 m 9 -> 0 (worst ramp-side now about -0.02, tortoise/turtle);
   flat lines identical to A-42.
3. Shots: DONE for deer/dog (viewed 800 px), skunk shot taken but shows no cloud.
   D:/PunchyMilestones/animal/a42b/: start-deer-before/after, start-dog-before/after (after dog is a different
   individual), sheet-start.png, start-skunk-cloud.png, gas-pair-800.png and skunk-gas-editor.png (editor).
   A-42 before frames: D:/PunchyMilestones/animal/a42/.
Notes: -255,27 is now a mountain; framing needs `-watch` + `-watchfrom`. No exceptions in player logs.
Overrun: ~58 calls vs 35, 1 build: the first build run lost framing on a mountain, and gas visibility took editor probes.

## A-43 (2026-11-02): leap blending, remote emote sounds, cave readout, F8, skunk gas material
Commit 3ba679b on animal-behaviours (pushed) on 89270af. ONE build to Builds/Windows (Succeeded). Hash v2 126 / 053C63D5
unchanged (F8 log counts 126 landmarks).
1. Leap blending: DONE. LeapGather eases with smootherstep over phase-scaled spans (no 0.08-phase snap); EaseGait
   slows the gait change near the sprint band for leaping runs (the legs swapped in ~0.1 s; top speed now ~0.3 s later;
   a player's dog body is not eased by this). Probe Temp/a43_leap.cs (Wolf hunt run), max joint change per frame,
   before -> after: 30 fps into 125.6 -> 77.4, stride 125.6 -> 81.1, out 72.8 -> 46.4 deg; 144 fps into 59.6 -> 20.6,
   stride 55.9 -> 19.9, out 27.3 -> 20.0 deg. (What remains at 30 fps is mostly the gallop stride itself.)
2. Emote sounds in multiplayer: DONE. Cause: remote bodies spawn with CreatureBody.Species -1, so no calls list.
   RemoteAvatar now carries the local species index (local only, no new network field). Host+client test (both Dog):
   each of Wave/Bow/Cheer/Dance logs once on the other machine, from another player's body at the emoter's position
   (host sees client at (2.5,163.6,1.5), client sees host at (0,163,0)). Logs Temp/a43_host.log, Temp/a43_client.log.
2b. Cave readout: DONE. Client after `-goto -3989,37,807`: `DebugControls: readout x -3989   y 37   z 807   facing 0°   in cave`
   (logged on entering/leaving a cave; y kept, no fall-through). F3 copies `-goto x,y,z`.
2c. F8: DONE. `F8 landmarks 126 before, 89 after leaving out caves: CaveEntrance 11, CaveNetwork 1, CrystalGrotto 4,
   FungalGrotto 3, Grotto 17, MineEntrance 1`. F7 and the landmark list unchanged.
2d. Skunk gas: DONE. New shader Punchy/Gas Puff (unlit, no depth fade, floor at 70% of its colour) and
   Resources/SkunkGas.mat; Puffs.mat and Soft Particle untouched. Build shot (Ashlands, dark flat ground, side-on,
   mid-flee): green cloud visible beside and behind the skunk (viewed 800 px), though not dense.
3. Shots (D:/PunchyMilestones/animal/a43/): before_strip_800.png / after_strip_800.png first (before/ and after/ hold
   leap-0..5 at frozen phases 0.2, 0.3, 0.34, 0.4, 0.7, 0.85, side-on in Ashlands); skunk-gas-0_800.png,
   skunk-gas-1_800.png, cave-f3_800.png; mp/ holds the two-client frames.
Notes: frames were picked by phase (-leapphase) in the before build, not in the editor; `-pose` respawns per line, so a
timed strip of one run is not possible, and frozen phases show key poses rather than the in-between blending (the
probe numbers carry that). `-pose ... scare` sets speed directly, so the eased take-off doesn't show in shots.
Untracked Assets/_Recovery left alone. A first build attempt returned Unknown (a compile error of mine, name clash);
fixed, then the one build. Calls ~78, builds 1.

## A-44 (2026-11-02): creature solid collision - PARTIAL, criteria not met

Commit cfbcee9 on animal-behaviours (pushed). Files: CreatureMotion.cs (Solid/Ground helpers), CreatureBrainSystem.cs,
FaunaDirector.cs, DebugControls.cs. All stay LF. No TerrainSource, player collision or network changes. Placement untouched, so the v2 126 / 053C63D5 hash is unchanged (not re-run).
- What it does: when a player is within 48 m, land animals check their own body (radius, length, height) against TowerWall and cave rock (below the surface only).
  A step into a solid slides to the nearest open way or stops. An animal already in a solid is pushed out sideways. Fleeing and wandering animals probe ahead and veer (boulders: RockAt at 4 Hz).
  Ground and rest height over cave entrances now come from FloorUnder, and wild spawns skip carved ground.
- Test flags: `-solidsoff` and `-solidstats`. Put them on EVERY shot-list line, because DebugControls re-applies options per line (my first after run had no stats for this reason).
- 8 min run, same build (5 cave mouths plus a 50+ creature line, a village and landmark 1), totals off -> on:
  - Ticks with a body sunk more than 0.15 m: 21748 -> 6909 (-68%)
  - Floating more than 0.2 m: 1742 -> 258 (-85%)
  - Stuck (boxed in): 0 s in both
  - Wall contacts after: about 3/min (21/min at the landmark minute)
  - Max sink is still metres (6-33 m) in some minutes, mostly where cave rock is mapped deep under a slope, plus landmark 1
- Cost: 1.0-1.6 solid queries per near creature-tick (23-64 per frame). A main-thread unburst Mono timing gives 0.8-37 ms per frame scaled to 50 creatures. This is only a loose upper bound and is not a Burst figure, so a real Burst/profiler figure is still owed.
- Not done: an editor sim of 3 species against a cave wall, a building and a tower (out of budget). The run's numbers cover small, medium and large (Dormouse, Deer, GrizzlyBear).
  Sink is not <= 0.15 m everywhere, and floating is not 0.
- Shots, D:\PunchyMilestones\animal\a44 (viewed at 800 px):
  - Pair: before\cave1-deer.png (old exe, deer hidden behind or in the cave-mouth rock) and after\cave1-deer.png (deer's head and back visible past the mound). Weak pair: the mound still partly blocks the camera.
  - Also cave1-bear and cave1-dormouse in both folders.
  - Same-build runs in stats-off\ and stats-on\. The logs are Temp/a44_soff.log and a44_on.log in the animal tree.
- Next: find what the remaining hidden ticks are (log the position and solid type of the worst one), tighten CountSolids versus Solid, and decide whether the deep-rock cases are real or a false count from the CaveDistance overstate. Calls ~85, builds 1.


## A-44b (2026-11-03): animal-behaviours 8cd0ce4
- Deep sinks were a false count: RockDistance used CaveDistance under any hillside (a nose a few cm under a slope counted ~20 m; 1e9 with no cave). Fixed: depth = nearer of (surface gap x normal.y) or CaveDistanceBound. Nose and middle tested at the ground there; stop at 5 cm in; buildings/props now solid for wild animals (FloraPlacement.Blocked).
- Player run (8 x 60 s, caves 1-5, crowd at cave 2, village, landmark): hidden >0.15 m 0 ticks (was 6,909), max sink 0.03 m, floating >0.2 m 0 (was 258), top-10 case lists empty. Contacts logged 0/min (worth a look).
- Editor sim: building and tower: 0 sink for Dormouse, Deer and GrizzlyBear. Cave wall: Dormouse 0.05 m, Deer 0.13 m, GrizzlyBear 0.30 m (6/300 ticks while pressing into a goal inside the rock).
- Cost: Burst job, one thread: about 0.4 ms per frame at 50 near (1.3-1.7 ms in 3 of 8 minutes).
- Hash 126 / 053C63D5. Shots: D:\PunchyMilestones\animal\a44b\cave1-{deer,bear,dormouse}-wall.png (deer viewed: whole body shows on the slope, but it isn't clearly turned at a wall).

## A-45 (animal-behaviours 
3abc2c4
)
- #54 ArmProbe Burst: 200 cave-air samples, 196 identical (max diff 0.0045 m); 0.486 ms managed vs 0.019 ms Burst per probe.
- #55 trace: due checks before NoSurface; #56 shared snapshot; #57 staggered searches. Per-frame numbers in perf2 lines below.
- Row 5 NOT met: bear max sink at 3 cave walls old 0.30/0.62/0.44 vs A-45 0.35/0.44/0.05 (target 0.15). Regression: dormouse stuck 27 ticks at wall c2. Contacts only counted for rock in KeepOutOfSolids; buildings go through WalksRoundWalls.
- Landmark hash read 127 / 4D245E12 (card expects 126 / 053C63D5) - not investigated, needs a look.
- Shots: D:\PunchyMilestones\animal\a45 (load, cave/approach/open before+after, 5-min flee old/new search).
    DebugControls: perf 'cave-before' 156 frames, frame 19.36 ms (max 24.7), no frame timing; cave chunks loaded 57, submitted 57, in view 20, casting 5, 201 k cave tris in view; lights 3 (0 shadowed), particles 6 systems / 364; camera probe 0.99/frame, 704 us/frame; IsRock 1.65 us/call (Burst); underground 1.00, hidden 1.00, player (-1653.3, 29.7, 2380.4)
    DebugControls: perf2 'cave-before' 156 frames, creatures 139; trace 13.864 ms/frame, 50.4 surface checks/frame; snapshot copies 23.8/frame, 0.032 ms/frame; searches/frame predator 105.0, alarm 124.9, chat 0.0; flees predator 0, alarm 0; totals flee 2/4
    CreatureSolids cases: sinks; floating
    CreatureSolids: on, 60 s, 3437 frames, 65053 creature-ticks near, contacts 0.0/min, stuck 0 ticks (0.0 s), hidden >0.15 m 0 ticks, max sink 0.00 m, floating >0.2 m 0 ticks, queries 9.4/frame (0.49 per creature-tick); cost now 40 near: 1.255 ms Burst one thread, 1.569 ms per frame at 50
    CreatureSolids cases: sinks; floating
    CreatureSolids: on, 60 s, 3598 frames, 58440 creature-ticks near, contacts 0.0/min, stuck 0 ticks (0.0 s), hidden >0.15 m 0 ticks, max sink 0.00 m, floating >0.2 m 0 ticks, queries 23.8/frame (1.47 per creature-tick); cost now 46 near: 1.459 ms Burst one thread, 1.586 ms per frame at 50
    DebugControls: perf 'cave-after' 180 frames, frame 16.69 ms (max 20.5), no frame timing; cave chunks loaded 57, submitted 41, in view 20, casting 5, 201 k cave tris in view; lights 3 (0 shadowed), particles 7 systems / 345; camera probe 0.99/frame, 22 us/frame; IsRock 1.63 us/call (Burst); underground 1.00, hidden 1.00, player (-1653.3, 29.7, 2380.4)
    DebugControls: perf2 'cave-after' 180 frames, creatures 139; trace 0.784 ms/frame, 0.7 surface checks/frame; snapshot copies 3.0/frame, 0.005 ms/frame; searches/frame predator 9.0, alarm 10.5, chat 0.0; flees predator 0, alarm 0; totals flee 7/5
    CreatureSolids cases: sinks; floating
    CreatureSolids: on, 60 s, 2508 frames, 50588 creature-ticks near, contacts 0.0/min, stuck 0 ticks (0.0 s), hidden >0.15 m 0 ticks, max sink 0.00 m, floating >0.2 m 0 ticks, queries 4.8/frame (0.24 per creature-tick); cost now 31 near: 0.361 ms Burst one thread, 0.582 ms per frame at 50
    DebugControls: perf 'approach-before' 157 frames, frame 19.29 ms (max 35.8), no frame timing; cave chunks loaded 55, submitted 37, in view 27, casting 25, 167 k cave tris in view; lights 2 (1 shadowed), particles 7 systems / 407; camera probe 0.99/frame, 538 us/frame; IsRock 1.60 us/call (Burst); underground 0.00, hidden 0.00, player (-1664.0, 67.8, 2531.2)
    DebugControls: perf2 'approach-before' 157 frames, creatures 151; trace 10.421 ms/frame, 72.8 surface checks/frame; snapshot copies 23.8/frame, 0.046 ms/frame; searches/frame predator 112.0, alarm 137.9, chat 0.0; flees predator 0, alarm 0; totals flee 43/5
    CreatureSolids cases: sinks; floating
    CreatureSolids: on, 60 s, 3590 frames, 79206 creature-ticks near, contacts 0.0/min, stuck 0 ticks (0.0 s), hidden >0.15 m 0 ticks, max sink 0.00 m, floating >0.2 m 0 ticks, queries 12.1/frame (0.55 per creature-tick); cost now 34 near: 0.658 ms Burst one thread, 0.968 ms per frame at 50
    DebugControls: perf 'approach-after' 181 frames, frame 16.70 ms (max 21.4), no frame timing; cave chunks loaded 55, submitted 37, in view 27, casting 25, 167 k cave tris in view; lights 2 (1 shadowed), particles 7 systems / 413; camera probe 0.99/frame, 20 us/frame; IsRock 1.51 us/call (Burst); underground 0.00, hidden 0.00, player (-1664.0, 67.8, 2531.2)
    DebugControls: perf2 'approach-after' 181 frames, creatures 145; trace 0.406 ms/frame, 0.7 surface checks/frame; snapshot copies 3.0/frame, 0.009 ms/frame; searches/frame predator 9.3, alarm 11.1, chat 0.0; flees predator 0, alarm 0; totals flee 44/5
    CreatureSolids cases: sinks; floating
    CreatureSolids: on, 60 s, 3593 frames, 85236 creature-ticks near, contacts 0.0/min, stuck 0 ticks (0.0 s), hidden >0.15 m 0 ticks, max sink 0.00 m, floating >0.2 m 0 ticks, queries 12.7/frame (0.54 per creature-tick); cost now 49 near: 0.032 ms Burst one thread, 0.033 ms per frame at 50
    DebugControls: perf 'open-before' 181 frames, frame 16.67 ms (max 16.7), no frame timing; cave chunks loaded 14, submitted 9, in view 0, casting 0, 0 k cave tris in view; lights 2 (1 shadowed), particles 7 systems / 522; camera probe 0.00/frame, 0 us/frame; IsRock 0.49 us/call (Burst); underground 0.00, hidden 0.00, player (-1846.0, 66.7, 3104.0)
    DebugControls: perf2 'open-before' 181 frames, creatures 96; trace 1.573 ms/frame, 69.5 surface checks/frame; snapshot copies 23.9/frame, 0.026 ms/frame; searches/frame predator 72.0, alarm 90.0, chat 0.0; flees predator 0, alarm 0; totals flee 50/5
    CreatureSolids cases: sinks; floating
    CreatureSolids: on, 60 s, 3598 frames, 61334 creature-ticks near, contacts 0.0/min, stuck 0 ticks (0.0 s), hidden >0.15 m 0 ticks, max sink 0.00 m, floating >0.2 m 0 ticks, queries 24.8/frame (1.45 per creature-tick); cost now 41 near: 0.028 ms Burst one thread, 0.034 ms per frame at 50
    DebugControls: perf 'open-after' 181 frames, frame 16.67 ms (max 16.8), no frame timing; cave chunks loaded 14, submitted 9, in view 0, casting 0, 0 k cave tris in view; lights 2 (1 shadowed), particles 7 systems / 480; camera probe 0.00/frame, 0 us/frame; IsRock 0.49 us/call (Burst); underground 0.00, hidden 0.00, player (-1846.0, 66.7, 3104.0)
    DebugControls: perf2 'open-after' 181 frames, creatures 95; trace 0.301 ms/frame, 1.0 surface checks/frame; snapshot copies 3.0/frame, 0.010 ms/frame; searches/frame predator 6.0, alarm 7.4, chat 0.0; flees predator 8, alarm 0; totals flee 58/5
    CreatureSolids cases: sinks; floating
    CreatureSolids: on, 60 s, 3598 frames, 43546 creature-ticks near, contacts 0.0/min, stuck 0 ticks (0.0 s), hidden >0.15 m 0 ticks, max sink 0.00 m, floating >0.2 m 0 ticks, queries 18.9/frame (1.56 per creature-tick); cost now 45 near: 0.033 ms Burst one thread, 0.037 ms per frame at 50
    CreatureSolids cases: sinks; floating
    CreatureSolids: on, 60 s, 3600 frames, 39769 creature-ticks near, contacts 0.0/min, stuck 0 ticks (0.0 s), hidden >0.15 m 0 ticks, max sink 0.00 m, floating >0.2 m 0 ticks, queries 17.9/frame (1.62 per creature-tick); cost now 44 near: 0.032 ms Burst one thread, 0.037 ms per frame at 50
    CreatureSolids cases: sinks; floating
    CreatureSolids: on, 60 s, 3600 frames, 34223 creature-ticks near, contacts 0.0/min, stuck 0 ticks (0.0 s), hidden >0.15 m 0 ticks, max sink 0.00 m, floating >0.2 m 0 ticks, queries 19.5/frame (2.05 per creature-tick); cost now 42 near: 0.035 ms Burst one thread, 0.041 ms per frame at 50

## A-46 (animal-behaviours 3142018, on A-45 3abc2c4; PR #73 open)

- **Cave walls:** met. Max sink in m, old / A-45 / A-46, from the editor sim (v1 world):
  - c0: bear 0.44 / 0.53 / 0.05; dormouse 0.14 / 0.05 / 0.04.
  - c1: bear 0.00 / 0.00 / 0.00; dormouse 0.07 / 0.00 / 0.00.
  - c2: bear 0.69 / 0.10 / 0.04; dormouse 0.08 / 0.04 / 0.04.
  - Dormouse stuck is 0 everywhere. KeepOutOfSolids now turns on the spot to a clear facing before it pushes.
- **#12:** the wander turning-circle cap (A/B `-oldwander`) is in.
  - Wander changes nothing: the sim gives songbird 22 and dormouse 9 deg/s, old and new alike, with 0 full circles, and the in-game wander is 0 deg/s.
  - The spins come from **Play mode**: songbird 124-148 deg/s, two 272 deg/1 s spins logged; marmot and goat too.
  - Not fixed. Play mode is the next card.
- **#46:** inconclusive. Ground and hover 30 m both gave 0 of 5-11 deer fleeing, mean 23-28 m. The deer were outside flee range at (277,-761). Needs a closer spot.
- **#57:** the flee-oldsearch and flee-newsearch 5-min lines were still running at the budget ceiling. The counts will be in a46/player.log ("flees this line"). Unreported.
- **#68:** done in code, but the tab doesn't show in editor play. Needs follow-up.
  - Built: CharacterTab.cs adds class -> species -> colour, remembers the last pick per class, and reads classes from species data (Upright, Habitat, Crawls). NetAvatars.LocalCoat feeds NetPlayerLook.Coat. NetMenu's Body row is gone.
  - Problem: in editor play the menu showed only Graphics, and `-pickclass` did nothing. The cause isn't known (tab registration or scene timing?).
  - Not shown yet: solo/host/client and the bird -> fish two-click count.
- **Hashes:** v1 103 / 19885131, unchanged. No landmark code was touched.
- **Build:** one build, made before the `-menu`/`-pickclass` shot-line flags.
- **Shots:** PunchyMilestones/animal/a46 (a46-load, spin-old/new, lift-ground/30m, player.log) and a46/editor (the Character shots show no tab).

## A-46b (2026-11-03) animal: finish A-46

Commits on animal-behaviours: 9c96b16 (#68 tab, #12 Play turn cap, "-oldplay"), 1e8dc1c ("-spawnradius" test flag). One build; shot list Temp/a46b_shots.txt. Shots: PunchyMilestones/animal/a46b (build), a46b/editor, a46b/net (host + client).
Hashes: v2 127 / 4D245E12 unchanged (build log). v1 103 / 19885131 not rechecked; no landmark or terrain code touched.

- #57 flees (spot -255,27 with Wolf 3, Deer 8, Marmot 8 spawned, 150 s each): old search predator 4 / alarm 3, new search predator 6 / alarm 19 (searches 576k vs 71k). Not within 10%: the counts are too small to compare (single runs, wolves hunt at random). Need a longer run or several runs to call it.
- #68 Character tab: cause was NetAvatars calling OnDisable (which removes the tab) when the client world changed, on the first frame. Fixed with ReleaseWorld. Tab shows solo (editor and build), as host with a client connected, and on the client (shots taken after the host quit and it carried on alone, i.e. across a world change). Bird -> Fish = two clicks (Character tab, Fish). Client log shows a NullReferenceException when it lost the host; not looked into.
- #12 Play spin: the runner (Play stage 1) re-aimed freely every frame, spinning at 200-300 deg/s when stuck. Its aim now turns at most 100 deg/s. Build, 120 s each: Marmot Play 87-96 deg/s old -> 57-69 new; Songbird barely played (new: one 7 s window at 111 deg/s); Dormouse never played (sleeps/climbs). Spins: 1 old (Monkey, Play stage 1), 0 new. Editor run: Marmot 99-103 -> 59-61, Songbird -> 35-68, spins 2 old -> 0 new.
- #46 flying height: build run had deer at 26-29 m (surface -spawn lands 6-45 m out), so added "-spawnradius". Editor, same -goflat spot, deer at 9-15 m: ground 0 of 10 fleeing, 30 m up 0 of 20 fleeing. Deer don't flee a standing player at that range even on the ground, so height can't be judged this way; needs a moving player or a different threat.
Left: #57 more runs; #46 test with a moving player; client-connected tab shot (host quit before client's shots); v1 hash check.

## A-47 (2026-11-03): stopped early, editor stuck, WIP pushed

- **Blocker:** the animal editor stopped working partway through. It hit a Burst error ("Unable to create a SharedStatic for this key") because the SearchCounts struct grew. After that it flipped between ready and unreachable. Recompile and eval requests timed out (504), and no new compile started. The SharedStatic key is now renamed (SearchCountsKey2), but the editor probably needs a restart before that takes. I did not kill anything.
- **Code pushed, not compiled or run since the key rename:**
  - animal-behaviours 7f128aa:
    - #59 brain LOD: creatures with no player within 60 m think every 3rd frame, staggered. Between thinks they coast on the ground, and they catch up the skipped time on their next think. `-nobrainlod` turns it off. The perf2 log shows brain ms, thinks per frame and LOD skips (marker Brain.Tick).
    - #68 client NRE fix: DebugControls rebuilds its queries when the client's world is replaced.
    - New test flags: `-liftpass v`, `-fixeddt` (logs flee counts and summed positions 60 s in) and a PlayerFlee counter.
  - animal-a47-split e6fb97a (side branch, not on animal-behaviours yet): #61 mechanical split into 7 files, the largest 1,223 lines. Not compiled.
- **No numbers yet.** Rows 1, 2, 3 and 5 were not run. Row 4 has no before/after comparison. The hash check was not run. No build was made.
- **Next (A-47b), after an editor restart:** recompile, then run Temp/a47_e1.txt in the editor. It covers the #57 3x300 s runs, the #46 ground vs 30 m pass, the #61 fixed-step lines and the #59 busy spot with LOD on and off. Then merge the split branch, rerun the #61 lines, check the hashes, and do one build plus the net run with a longer host list.

## A-47b (2026-11-03): blocked on RAM, only the merge done

- **Blocker:** free RAM never reached 7 GB. It was 5.5 GB at the start, 4.8 GB at the last check of the 60 min wait, and 3.1 GB at 20:52 with the world and player editors open. I never opened the animal editor, so rows 0 (editor part), 1, 2, 3, 4 and 5 did not run. No numbers, no hashes, no build.
- **Row 0 (git part) done:** animal-behaviours 937706d merges origin/main (PR #89) with a merge commit. The merge was clean and is pushed. TraceSystem now has the Burst GroundJob after the A-45 cheap-checks-first order. I kept it as it is.
  - Note: `-oldtrace` now sets both OldTrace (#81, managed lookups) and OldOrder (#55, every creature sampled). An "old" run is therefore old on both counts.
- **The import-worker settings were not applied** because that needs the editor. ProjectSettings/EditorSettings.asset is unchanged.
- **Split merge (row 2) is ready to resolve:** `git merge-tree` of HEAD and animal-a47-split conflicts only in CreatureBrainSystem.cs. Take the split's file, then re-apply main's two W-57 hunks:
  - the hermit Stage line (`AnchorSide >= 2 ? AnchorSide : 1`) stays in CreatureBrainSystem.cs, around line 929;
  - `s.AnchorSide = 1;` becomes the Variety form in CreatureBrainSystem.Folk.cs, line 204.
  - The 6 new partial files have no .meta files yet. Unity makes them on import, and they must be committed.
- **Next (A-47c):** the same card once there is 7 GB free, for example when one of the other editors is closed.

## A-47c (2026-11-03): rows 0-5 run, editor closed

- **Commits (animal-behaviours, pushed):** d5f7e59 merges origin/main 913d1b2 (PR #90 P-1 settings, PR #91 v3 island flag), clean. a771ac1 fixes a compile error from A-47: the `-fixeddt` log line called the shot loop's local `Arg()` (CS0103). That error sent the editor to the Safe Mode prompt; I closed my own stuck editor and reopened it. a172c51 merges animal-a47-split (#61) and re-applies main's two W-57 hunks (hermit Stage line in CreatureBrainSystem.cs, the Variety AnchorSide in CreatureBrainSystem.Folk.cs). The 6 new .meta files are in. All files kept LF.
- **Row 0, import workers:** in Unity 6000.5 these settings live in EditorUserSettings, not EditorSettings. Standby is now 0 and the idle shutdown 60000 ms, saved in UserSettings/EditorUserSettings.asset. That file is git-ignored, so there was nothing to commit. Each tree has to set it on its own machine.
- **Row 1:** clean compile, no Burst or SharedStatic errors after the fix and after the split.
- **Row 2 (#61):** the before and after numbers don't match, but the test can't tell either way. `-fixeddt` isn't deterministic. At 60 s, e1-split-a1 before the split gave flees predator 1 / alarm 0 / player 1, sim 100, sum (-27021.80, 12381.23, -907.44). After the split it gave 0 / 1 / 1, sim 94, sum (-24300.40, 12541.57, 28.06). The live sim count differs at the same step, so spawns or ambient life don't repeat from run to run. Lines also inherit the last line's creatures. The split compiles and behaves normally, but sameness is not proven. A repeatable check needs fixed spawn seeds plus a world reset per line.
- **Row 3, after the split:**
  - #57, flees (predator+alarm) per 300 s: old search 379 / 92 / 17 (mean 163, wide spread), new 90 / 48 / 35 (mean 58). Searches: old 1.4-2.0 M, new 0.24-0.27 M (6-8x fewer).
  - #46, deer within 40 m that flee at sample 1: ground 10/10 and 15/18, 30 m pass 0/11 and 0/11.
  - #59, busy spot brain ms: LOD off 0.749 (129 creatures) and 1.480 (233); LOD on 0.895 (184) and 1.126 (264). Per creature that is about 5.8-6.4 µs off and 4.3-4.9 µs on (about 25% less). Thinks per frame drop about 50%. The populations grow line by line, so these are not matched pairs.
- **Row 4 (#68):** one build (Succeeded, 0 errors, 2:24). Host+client run done. Connected-client shot: D:\PunchyMilestones\animal\a47c\net\c-mid.png (75 sim live). After the host quit, client.log has no NullReferenceException. The client did log one NRE at join time, before c-connected: EntityQuery.TryGetSingleton inside DebugControls.ShotList, on a query from the replaced world. That is the same family as #68, at a spot the fix didn't cover. Left for A-48.
- **Row 5, hashes (measured):** v2 136 landmarks / B5EFF8C8 (editor and the player's bake line). v1 103 / F628AE5F (was 19885131). This v1 shift arrives with main's PR #91 IslandV2 changes; animal code doesn't touch landmarks. Note: the "v2 shared with ... (no rebake)" line in host and client logs prints 728F95DF for the same island, so the shared-world hash call reads differently (probably the palette lookup). Worth a look by world.
- **Split, for other lanes:** CreatureBrainSystem is now partial across 7 files (Activities, Folk, FolkWork, PetsCavesFlight, Places, SensesMovement and the main file). Rebase brain edits onto a172c51 or later and expect conflicts in CreatureBrainSystem.cs.
- Shots and logs are in D:\PunchyMilestones\animal\a47c (pre/, post/, net/). My editor is closed.

## A-48 (2026-11-03): ShotList null ref at join; #63 sky and perch ground caches
- **Commits (animal-behaviours, pushed, merged origin/main 3a4acb8 first):** f0be45c guard DebugControls' cached queries against a missing or replaced world; 5fe1f85 (#63) cache sky-bird cell grounds and songbird ground perches. Note: f0be45c alone doesn't compile (its log line uses the counters added in 5fe1f85); build from 5fe1f85.
- **Row 1:** new `QueriesLive()` rebuilds the cached queries when `World.DefaultGameObjectInjectionWorld` changes and returns false if the world is gone. ShotList, `-weather` and `-camrel` check it before `TryGetSingleton`. One player build (Builds/Dev, 1:27, 0 errors). Two host + client runs: **0 exceptions in host and client logs both times**. Run 1 (client 12 s after host, a48\net): the join timed out ("NetMenu: Couldn't reach 127.0.0.1:7979") and the client fell back to solo with a new world, the path that used to throw. Run 2 (client 45 s after host, a48\net2): the client got "connection 1 Connected", then lost it ("disconnected (ClosedByRemote)", host side Timeout), which covers the lost-connection path. Both clean.
- **For multiplayer:** in both runs the connection dropped while either side was still loading. A-47c's client.log also says "Couldn't reach", so its "connected" shot was actually solo.
- **Row 2, #63 (partial, no art):** GatherSkyBirds sampled the managed terrain 9 times every frame. It now samples each sky cell's ground once, cached per island and capped at 256 cells. Log: `ground samples sky 9 ... by frame 7951` (host, run 1) and 9 by frame 7370 (run 2). The old code would have taken about 71k samples, roughly 1.1 s of main thread at 16 us each. The client logged 18, because its world was replaced once. AmbientLife.Perch now caches each ground perch height (cleared on Reset, capped at 2000). It read 0 perch samples because the spot (-255,27, snowy mountain) has no songbirds, so that cache is untested. Audit: FaunaDirector's samples happen at spawn or per event, CreatureRenderSystem's cull is already Burst (CullJob), cricket heights are already cached, and AmbientLife:282 takes one sample a frame. #63 can stay open for whatever the profiler shows next.
- Exceptions: 0. Issues: #63. Calls: about 52. Logs in D:\PunchyMilestones\animal\a48 (net, net2). Editor closed.

## A-49 (2026-11-03): fear by height (#46), wander spin (#12), needs cue (#64)
- **Commits (animal-behaviours, pushed):** merged origin/main f405348 (PR #95, #96, fast-forward). 7e05224 adds test flags only, no behaviour change: `-goflat N` (Nth flat spot, each 150 m from the earlier ones, so every line gets a fresh herd), `-thirsty` (holds Thirst at 1) and `-watch Species:Mode` (e.g. Deer:Drink). LF kept.
- **Row 1 (#46): already works, no code change.** The height-weighted fear distance (HeightFear 2.5 above a 1.5 m gap) is already in. Run 1 lines carried alarm over into each other, so I added -goflat. Fresh herds of 10 deer, 8 m/s pass, deer within 40 m fleeing at sample 1: **5 m: 10/10 and 10/10 (player flees 4, 1). 30 m: 0/10 and 0/10 (player flees 0, 0)**, with the herd 4.8 m away horizontally. Ground pass: 10/10 (player 4). With `-flatfear` (plain 3D distance, no extra height weight), 30 m also gives 0/10 twice. A 30 m glide is already beyond the deer's 3D fear radius, so the weighting only matters between about 5 and 20 m up.
- **Row 2 (#12): no tuning, measured.** `-spincheck`, Songbird:16 + Dormouse:8 at Start, 120 s per line. "Old" means `-oldwander -oldplay` (A-46 caps off). Wander has no spin left: Dormouse Wander/ground 0.0 deg/s in every line, day and night, old and new, and Songbird Wander/ground 0.0. Songbird Wander/flying with the cap off: 6.1-19.9 deg/s; with the cap on (day new): 0.0-0.5. The fast turning is in **Play**: night old Songbird Play 106.5 deg/s, Dormouse 116.4; night new Dormouse Play 77.0 (cap on). Flee 40-47 deg/s. If the spin Zach sees is still there, it's the Play chase, not wander. Next step, if wanted: lower the Play turn cap, or give Play a minimum radius for small species.
- **Row 3 (#64): proposal (needs design, Zach decides).** Drink (walk to water, head down, lap) already exists, but my e2-drink shot (riverfall, -thirsty, -watch Deer:Drink) framed the waterfall and caught no drinking deer, so there is no cue shot. Proposal:
  1. Tired: slower gait, lower head and a short yawn before Sleep (pose only, from synced mode/speed).
  2. Hungry: sniff-walk with the head low between grazes, longer grazes; thirsty animals head to water in a line (already the Drink path).
  3. Lonely: more calls and drifting toward the nearest herd. Needs stay server-only; clients read the cue from mode and pose, so nothing new is sent.
- Exceptions: none seen. Shots and log: D:\PunchyMilestonesnimal49 (s2\, editor-run2.log). Editor closed (PID 14180; Punchy-world 55872 left alone). Temp/PlayArgs.txt removed (none before). Calls: about 70 (over the ceiling: run 1 was spoiled by herd carry-over and missing shots, so I ran a second pass).

## A-51 (2026-11-04): main-thread wins from profiler job 15 (#62)
Branch animal-behaviours, pushed: b6cf915 (row 1), edabfd7 (row 2), b527dc1 (row 3). Each row is its own file and commit.
Measured in the editor, 300 frames per stop, uncapped (-fpscap 0), with Burst safety checks, the jobs debugger and leak detection off. Numbers are main-thread ms per frame, before and after.

| Row | System | Approach | Start (-place Start) | Hut cave (-goto 530,45,958) |
|---|---|---|---|---|
| 1 | CreatureRenderSystem | (a)+(b): CullJob parallel-scheduled, compaction in a Burst CompactJob while the main thread gathers critters, birds and fish. (c): poses drawn one frame late (PoseJob completed next frame) | 0.524 -> 0.214 | 0.369 -> 0.281 |
| 2 | InteractionInputSystem | (c): reads last frame's ReachJob result; no Complete | 0.315 -> 0.018 | 0.293 -> 0.022 |
| 3 | Debug HUD | Off by default; F1 or `-hud` shows it | OnGUI 2.454 -> 2.045 | n/a |

- PlayerLoop: Start 6.25 -> 4.97 ms; hut cave 6.17 -> 5.63 ms.
- Row 3:
  - The OnGUI marker also counts other IMGUI callers, so the readout itself was about 0.4 ms.
  - Change: F1 now toggles only the readout. The interaction prompt stays visible; `-nohud` still hides everything.
  - The bench and shot flags were not touched.
- Behaviour: same creature counts and the same pose job. The only visible change is that poses draw one frame late (about 7 ms at these frame rates).
- Coordinator's PoseJob batch note: the batch size is still 8, but the pose job is no longer completed in the frame it starts, so workers absorb it. Not re-measured on the town walk or the wharf.
- Exceptions: none from these systems. Editor closed gracefully. Temp/PlayArgs.txt removed (there was none before).

## A-50: stopped on supervisor order (waits on P-7)
- Spawner, posts and stand-in work were tried in the editor but not committed. The local cherry-pick of d7ed5eb was dropped, so the branch is unchanged.
- Results from that try:
  - Stand-in Cave picks: hash 0xf1e10e91 (6 creatures).
  - Stand-in Crypt picks: hash 0x3ac96bb1 (8 creatures, with Ghost, BoneHound, Altar, SkullNiche picks). The two types give different picks.
  - Guard log lines appeared: "Bat guards its chest: faces a player 3.5 m away".
  - The files are kept off-repo for when P-7 lands.

Calls: about 80 across A-51 and the aborted A-50 start (A-51 alone was about 55).

## A-52 (#102 fauna build hitch), 2026-11-04

- Commit f2ff575 on animal-behaviours (pushed).
- Profile (editor, safety/jobs debugger/leak detection off, uncapped): species 0-3, prey 3-4, blobs 8-30, capsule 0-2, carry 2-6 ms; flora 2170-2317 ms. The fauna's obstacle pass (`FloraPlacement.Build(obstacles: true)`) was ~95%: it grew full 3-LOD meshes and support grids it never reads.
- Fix: `FloraGenerator.Grow(species, variant, shapeOnly)` (additive optional flag: LOD 0 parts only, no support grid, no meshes); the obstacle pass passes `shapeOnly: true`. **World lane: additive change in shared FloraGenerator.cs / FloraPlacement.cs; default behaviour unchanged.**
- Build frame before -> after: v2 2347 -> 798 ms (flora 2209 -> 687); v3 2208 -> 609 ms (flora 2170 -> 588).
- Fingerprint (FNV-1a of species names + serialized creature/carry/flora-obstacle blobs, 112 species): E698F061 before and after, v2 and v3. Logged as `[fauna build]` lines, editor only.
- Not done: Burst/Parallel.For of the remaining grows (Grow touches UnityEngine.Object, main-thread only) and overlap with the world's flora build; the rest is now ~0.6 s, left for a follow-up if still needed.
- Calls used: ~55.

## A-50 (2026-11-04): #24 dungeons phase 2, spawner + dungeon behaviours
- Commits on animal-behaviours (pushed): cd9dda5 (merge of player-client be6d005: P-7 TempleSockets), d243ab7 (spawner, posts, stand-in). New animal-owned folder Assets/Game/Scripts/Fauna/Dungeon/; Assets/Game/Scripts/Dungeon/ is only read.
- Row 1 done: weighted tables keyed by biome + dungeon type + socket kind + size, one seeded pick per DungeonSocket (Scrambled socket seed), creatures + placeholder item boxes. The spawner reads any entity with a DungeonSocket buffer, so TempleSockets is picked up with no glue.
  - Sunken temple (68 sockets from 47 rooms, socket hash D58F4D47): picks hash 0x7c8c8b71, picked again 0x7c8c8b71 (same); creatures 13, chests 5, wall 12, interactables 13, clutter 13.
  - Same sockets picked as other types: cave 0x5bbaf48f/37 filled, crypt 0x691230ca/54, ruin 0xde111b4a/54, mine 0x911717db/47; so the tables differ by type.
  - Cave stand-in (-dungeontest only, 24 sockets at the hut cave, gives the NearWater sockets the temple doesn't have yet): 0xa9995035, same again.
- Row 3 done in editor Play as host (ServerWorld + ClientWorld in one editor): identical hashes and pick lists in both worlds, 0 exceptions after the fix below. No separate-process join: the one player build crashed (stale Burst dll from a 30 s eval timeout), and the rebuild couldn't overwrite Builds/Dev lib_burst_generated.dll because the two crashed Punchy.exe stay running (couldn't stop them). **Supervisor: kill the stuck Punchy.exe processes before the next build there.**
- Fixes along the way: Burst Debug.Log doesn't take F1 (ints instead); the spawn loop now copies the socket buffer first (director.Spawn is a structural change, which invalidated the buffer and spawned duplicates every frame).
- Row 2 NOT proven: DungeonPost (guard at NearChest: stay within the leash, face the nearest player; drink at NearWater) is in, the server logs "spawns animals" and the species lookups all resolve, but a later query found 0 DungeonPost entities in ServerWorld and no drink/guard log lines; the server-side player sat at (0,0,0), not at -goto. Next: check whether the director culls/recycles the dungeon creatures (they aren't in its tier counts) and retest with a player inside the temple.
- The stand-in stays until a set piece writes NearWater sockets (comment in DungeonStandIn.cs says so).
- Calls used: ~62 (over the ceiling; the build crash cost ~10).

## A-50b (2026-11-04): #24 row 2, dungeon creatures kept and posted
- Commit on animal-behaviours (pushed): 8b9d708. Two causes of 0 DungeonPost: (1) FaunaDirector.Despawn let the dungeon creatures go by distance (no player near the temple; they counted as wild/settled/cave); (2) a director rebuild (terrain re-bake, the second "[fauna build]" right after the stand-in filled) destroyed every animal while the dungeon stayed marked filled.
- Fix: every dungeon creature gets a DungeonPost (Behaviour None too); the director's wild query skips DungeonPost, so they're their own tier (never let go by distance, not in the caps); DungeonFilled records director.Builds and a rebuild makes the creatures again (props not repeated).
- Retest, editor Play (local world, -dungeontest), player moved by eval to the temple and the stand-in cave: posts 24 (guard 3, drink 3), still 24 over a minute later.
- Log lines: "[dungeon] Default World dungeon 81: animals rebuilt, 11 creatures made again at their posts"; "[dungeon] Ghost guards its chest: faces a player 35 dm away, 0 dm from the chest" (temple); "Tarantula guards its chest" (stand-in); "[dungeon] CaveSalamander drinks at its NearWater socket (526, 958)", also GiantSpider and GlowMoth. 0 exceptions.
- Not retested here: host/client netcode worlds (this editor ran the local world). Editor closed.
- Calls used: ~33.

## A-53 (2026-11-04): audio master hook, cave FPS, CreatureState split (plan only)
- Row 1 done: f776f88 (animal-behaviours, pushed). AudioListener.volume = focus x Master; Master dropped from the voices
  (one-shots and loops), so the weather's own source follows master too. Editor Play logs:
  `-settings master=0` -> `GameAudio: listener volume 0.00 (focus 1.00 x master 0.00)`; `master=0.5` -> 0.50; reset to 1.
  Player lane: PlayerOptions.cs line ~124 comment ("AudioListener.volume don't read GameAudio.Master yet") is now stale.
- Row 2 (#81), profiled only, no fix committed. 530,958 is the **v2** bat folk chamber (`cave 26 chamber 525,45,958`);
  on v3 (layout B seed 8121) there is no cave within 120 m of it; the v3 bat folk chamber is -2674,23,-608.
  Editor Play, uncapped, 240 frames, main-thread CPU ms (median / p95 / PlayerLoop):
  - v2 cave floor 527,40,958: 8.14 / 9.94 / 6.06. v3 bat folk cave: 5.21 / 6.19 / 3.63. v3 surface at 530,958: 8.50.
  - v2 spot top fauna (ms/frame, total): CreatureSnapshotSystem 0.80, TraceSystem 0.32, HeldPoseSystem 0.31,
    CreatureRenderSystem 0.29, CreatureAudioSystem 0.14 (fauna ~1.9 ms). Brain 0.04, dungeon posts not in the top 25.
  - World-owned: CaveSystem.Update 0.53 (largest single Punchy cost), AmbienceSystem 0.34, WeatherSystem 0.24.
  - CPU is not "dreadful" in the editor, so the drop Zach saw is likely GPU-side (cave lighting, transparent pool or
    particles) or build-only. Next: a v2 build bench at that spot with GPU frame timing, then fix the snapshot sync
    (CreatureSnapshotSystem's cost is mostly waiting for the jobs that write CreatureState).
- Row 3 (#58) not started (call ceiling). Plan: move the brain-only fields into a new `CreatureMind` component
  (LookAt, LookEntity, LookKind, Timer, Partner, Leash, Home, ChatCool, ChatResume, LodDebt, LodCoast, RouteLeg,
  ChaseCooldown, WeaponCalm, Unsettled, TargetHeight). CreatureState keeps what movement, animation, rendering, audio
  and replication read (Position, Yaw, Speed, GaitSpeed, Phase, Mode, Scale, Action, Lie/Sit/Fly...). Brain jobs get
  CreatureMind RW; the snapshot and ghosts get a smaller CreatureState. Mostly CreatureBrainSystem.* plus DebugControls.
- Calls: ~42 of 45. Editor closed. Temp/PlayArgs.txt removed.
## A-53b (2026-11-04): #58 CreatureState split, step 1
- 0462c21 (animal-behaviours, pushed, on main 0ec7a8d): new `CreatureMind` component (LookAt, LookEntity, LookKind,
  LodDebt, LodCoast, ChatResume, RouteLeg, ChaseCooldown, WeaponCalm, Unsettled) leaves CreatureState. The brain job
  copies it into a job field per tick and writes it back, so no helper signature changed (Needs, BeginChat, EndChat lost
  `static`). FaunaDirector adds it on spawn (host/local only; ghosts and avatars don't get it). DungeonPosts writes
  m.LookAt. Compiles clean.
- Editor Play, uncapped, about 3.5 min, shot list `-warp town 1 -wait 90 -perflog` x2, 0 exceptions/errors.
  Fauna fingerprint E698F061 before and after. Brain ms (180 frames): busy1 0.616 (175 creatures) -> 0.638 (180);
  busy2 0.971 (134) -> 0.966 (127). Same within noise, as expected for a mechanical move.
- Step 2 not done (call ceiling): Timer, Partner, Leash, Home, ChatCool, TargetHeight. They are not brain-only:
  FaunaDirector spawn setup (~25 writes), Interactions (Timer, Leash in Tame, TargetHeight, Partner), FolkTalkSystem
  (Leash, ChatCool, Partner, Home), DungeonPosts, DebugControls, and the brain reads other creatures' Partner, Leash and
  ChatCool from the snapshot (would need a Minds array). Suggest keeping Leash/Partner/Home in CreatureState
  (Tame and FolkTalk may run off the host) and moving only Timer, TargetHeight and ChatCool in a step 2.
- Stale comment: player lane's PlayerOptions.cs line 124 ("AudioListener.volume don't read GameAudio.Master yet").
- Calls: ~38 of 40. Editor closed (PID 23316, mine); Punchy-world editor left alone. Temp/PlayArgs.txt removed.
## A-54 / A-54b (2026-11-04): flee height, spinning wanderers, brain LOD
- Merged origin/main (ba1ce3c) into animal-behaviours: b861a73. Commit 0d64408 (pushed): brain LOD 80 m / every 4th
  frame with a scrambled per-creature phase, `-brainlod R,N` A/B flag, and a flips-per-minute count in `-spincheck`.
- Row 1 (#46): the height weighting was already in (HeightFear, `-flatfear`); no new commit. Editor log, 10 deer
  spawned 6 m round the player, first sample after 20 s: on the ground 10 of 10 and 18 of 20 fleeing; hovering at
  40 m over the same spot 0 of 13 and 0 of 12 (mean distance 9-10 m across, gap 40 m).
- Row 2 (#12): no wander spin left to fix. `-spincheck`, 16 songbirds + 8 dormice at Start, four 30 s windows:
  Songbird Wander/ground and /flare 0.0-0.2 flips/min (0-2 deg/s), Wander/flying 0-9/min (one 2 s sample 32/min);
  Dormouse Wander/ground 0.0 flips/min every window. The turning that's left is Play (songbird chase: 16-29
  flips/min at 54-95 deg/s, ~1 m/s), plus GlowMoth round its light (~90/min, intended) and bats (~20/min). If Zach
  still sees songbirds spin, it's likely their Play chase: a judgment call whether to calm it. No fix committed, so
  no after number.
- Row 3 (#59): brain ms (summed over workers) at the HumanVillage spot (-255,27), uncapped, ~450 frames each. Spawns
  pile up line by line, so compare per creature: old (60 m, every 3rd) 0.553 ms/143 = 3.9 us and 1.198/244 = 4.9 us;
  new (80 m, every 4th) 0.899/197 = 4.6 us and 1.307/275 = 4.8 us. Thinks per creature per frame 0.48-0.50 old,
  0.52 new (the wider range keeps more of the spawn ring "near"). Same within noise: the change evens out the phase
  but doesn't save time at this spot. Near/far split not measured separately. Fauna fingerprint 51E641E9 (was
  E698F061 before the main merge; the merge changed it, the LOD can't). About 9 min of Play, 0 errors/exceptions.
- Row 4 (#58 step 2) not done (optional, budget).
- Calls: ~33. Editor closed (PID 61544, mine, graceful); Punchy-world and Punchy-player editors left alone.
  Temp/PlayArgs.txt removed. Shots in D:\PunchyMilestones\animal\a54.
## A-55 (2026-11-04): load time, brain searches, brain file split
- Row 1 (#102), 6c441fa (animal-behaviours, pushed): the FaunaDirector build frame was almost all the flora lookup's
  shape-only grows (flora stage 570-630 ms). FaunaDirector now starts them on worker threads
  (FloraPlacement.StartObstacleShapes, FloraGenerator.GrowShape) and builds once they're done, 1-2 frames later
  (10 s cap). Editor build frame: v2 735 -> 27 ms, v3 605 -> 21 ms; flora stage 634 -> 1 ms. Fauna fingerprint
  51E641E9 before and after on both. `-syncshapes` keeps the old path for A/B.
- Row 2 (#57): the stagger was already in (A-45: SearchDue, SearchPeriod, `-oldsearch`); nothing left to stagger
  except a fleeing animal's predator search (kept every frame on purpose) and the rare random chat searches.
  No commit. HumanVillage town 1, uncapped, 90 s wait: `-oldsearch` brain 1.068 ms (195 creatures, predator
  4.7 / alarm 5.6 searches/frame) vs default 1.089 ms (192, 4.2 / 5.1). The near-equal search counts suggest
  `-oldsearch` from Temp/PlayArgs.txt didn't take (it may only apply per shot line); A/B unconfirmed. ~8 min of
  Play over the card, 0 exceptions.
- Row 3 (#61) no commit: the split was done in A-47c (7 partial files). What's left in CreatureBrainSystem.cs
  (1243 lines) is mostly Think(), one ~875-line method (lines 366-1240) whose sections share locals, so it can't
  be split into files mechanically; it needs extracting into methods (a refactor card with a fingerprint check).
- Calls: ~47 (ceiling hit). Editor closed (PID 34164, mine); Punchy-world (21836) and Punchy-player left alone.
  Temp/PlayArgs.txt removed.

## A-56 (2026-11-04): dungeon chests and loot, -oldsearch A/B
- Commit b397c2e (animal-behaviours, pushed, on top of main 2dae74b). Rows 1-3 landed in one commit by mistake
  (message covers #24; it also holds the #57 DebugControls change).
- Row 1 (#24): MakeProp skips Chest sockets (Items draws the chest) and the stand-in boxes use Resources/ItemMaterial
  tinted via MaterialPropertyBlock _BaseColor. Shot a56-temple-chests: inside room 9, props tinted, no magenta seen
  in a small check; a chest isn't clearly framed. Note: `-place temple` lands at the surface SunkenTemple
  (1320,-2720), not the socket temple (3519,19,360); the first shot-list `-goto` lands on the sand above it.
- Row 2 (#24): PickLoot, 3-6 loose items on empty Creature/Clutter sockets from the folded socket seeds; host/solo
  Items.Spawn. Log, same in 5 runs: "loot: dungeon type 1 seed 0x16503f99: 4 items, 4 spawned: Coinx4@59
  Stonex1@1 Mushroomx2@0 Stonex2@8". Shot a56-loot-stone (unchecked; camera may be wall-blocked).
- Row 3 (#57): cause found: each shot line reset the flag. Now sticky + logged ("-oldsearch ON"). HumanVillage,
  uncapped, 60 s each, one session: new brain 0.592 ms (124 creatures, 2.9/4.0 searches/frame) vs old 0.281 ms
  (97, 22.5/28.4). Flag confirmed (8x searches); ms not comparable (creature counts differ), searches look cheap.
- Fauna fingerprint not measured. Calls ~46 (ceiling). Editor closed; 0 exceptions.

## A-57 (2026-11-04): A-56 checks, fair -oldsearch A/B, brain split

Commits on animal-behaviours: 58b75dd (merge origin/main, far shadows), 2567e10 (#61 Think split). Rows 1-3 have no code commits.

| # | Result |
|---|---|
| 1 | Fingerprint 51E641E9 on 58b75dd, the same as main 2dae74b. b397c2e only touches DebugControls/DungeonSpawner and main's new commit is render-only, so b397c2e was not measured on its own. |
| 2 (#24) | Shot a57-temple-mw.png: camera at the mushroom loot spot (3513,19,368) looking west. It shows a chest and the loose Mushroom x3 (with its pickup prompt), and the magenta-pixel count is 0. Checked on a 200 px contact-sheet tile, not a separate crop. The temple's glow crystals are purple/pink, not magenta. Loot seed 0xf9205369 gave 3 items, where A-56 had 0x16503f99 with 4. Not investigated. |
| 3 (#57) | Same session and spot (HumanVillage -255,27), uncapped, alternating 15 s windows, 61-73 creatures. Brain time per creature: old 3.43/3.66/3.00 us (mean 3.36), new 3.16/3.10/2.97 us (mean 3.08), about 8% cheaper. Predator/alarm searches per frame: old 10.5-13.8 / 14-18, new 0.8-1.3 / 1.3-1.9. Verdict: **keep**. 0 exceptions. |
| 4 (#61) | Think went from about 875 lines to 286. Its sections became named methods with summaries: Sense, React, StartFleeing, Haunting, Errands, Idling, Wandering, Fleeing, Sitting, Working, ChildPlay, KeepToLeash, Steer, Posture, CarriedItem. It compiles and the fingerprint is unchanged (51E641E9). |

Shots: D:\PunchyMilestones\A-57\ (a57-old1-3, a57-new1-3, a57-temple-*). The editor was closed gracefully. Calls used: about 44 of a ceiling of about 45.
Left: a dedicated crop check of the temple shot if wanted, and why the loot seed changed since A-56.

## A-58 (2026-11-04): need cues, character save, loot seed cause

Commits on animal-behaviours (pushed, on main ccd2151): f56d361 (#64 need cues), 9521bdd (#68 body save/restore). Stopped at the call ceiling.

| # | Result |
|---|---|
| 1 (#64) | The 3 spare fear-mark capsules now show the strongest need over 0.6 when not fleeing: tired = pale bubbles rising, hungry = dust puffs at the nose, thirsty = falling drop, lonely = pink heart. Each shows 2.6 s of its own 8-15 s cycle, from a scrambled Individual, so a herd doesn't show cues in step. Render only. Fingerprint 51E641E9 after, the same as A-57. Compiles, 0 exceptions. **No usable shot**: a58-needs1-5 are mis-framed (v3 island, so the village spot is snow, and the eval move put the player in a cave). Needs are host/solo only (not sent), so clients see no cues. |
| 2 (#68) | The Character tab (class -> species -> colour) already exists from A-46. It is hooked by the one line `MainMenu.AddTab("Character", 5, ...)` in Net/NetAvatars.cs OnEnable (player lane, unchanged). The missing part was keeping the body across sessions. FaunaDirector now saves `Punchy.Character.Body` when the body changes mid-session and restores it (and that species' saved colouring) on the first avatar build. `-body` overrides it. **Not tested in Play**: no tab shot and no log of a restore after restarting Play. |
| 3 (#24) | Cause: the island version. Socket seeds are MakeSeed(worldSeed, dungeonId, i), and dungeonId is the temple's landmark *instance index*. On v2 the temple is instance 126 (seed 0x16503f99, 4 items); on v3 it is instance 99 (seed 0xf9205369, 3 items). The temple is at the same spot. A-57's second session and today's editor ran v3 (the log says "[island] v3 (B2...)"; PUNCHY_ISLAND or -island at editor launch, not set in this shell). Matching runs: v2, 6 runs in A-57's Editor-prev.log all 0x16503f99. v3: A-57 and today (ccd2151) both 0xf9205369 with the same 3 items. Fragile: any change to the landmark list re-seeds the loot. Suggestion for the Dungeon owner: key the id on the temple's position, not its index. |

Shots: D:\PunchyMilestones\A-58\ (a58-needs1-5, not usable). The editor was closed gracefully. Calls used: about 49 of a ceiling of about 48.
Left: a framed #64 shot (spawn a herd next to the player, or launch with v2 and go to the village), the #68 tab shot plus a restart-Play log, and a same-commit v2 rerun if wanted.

### A-58b (follow-up)
- #64: added the `-needcues` debug flag (forces need cues on, commit on animal-behaviours). The framed shot still failed: two tries (Deer:6, watch 1.5-2 m up, 4-5 m off, `-goflat -weather Clear`) both came out as flat grey haze with no creatures readable. a58-needs.png is not usable. Next: find out what fills the frame (camera in fog volume or in terrain?) before retrying.
- #68: picked Goblin, colouring 2 in the Character tab (a58-character.png, not reviewed). Stopped and restarted Play; log: `[character] body Goblin saved for next time` then `[character] restored body Goblin, colouring 2`. PlayerPrefs left set to Goblin/2.
- Note for later: clients don't see need cues (needs are simulation-only and not sent to clients). Not fixed.

## A-59 (2026-11-04): need cues for clients, needs split; shot and client test not finished

Commits on animal-behaviours (pushed): merge of origin/main 9a6f896, a49e354 (#64 cue byte + #58 needs move). Stopped over the call ceiling.

- Setup: PlayerPrefs body was Goblin (left by A-58b); deleted the body key and the per-class colouring keys, so the default body is back.
- Row 1 (#64): cause not proven. The camera is not inside the ground or water: during the deer watch hold it sat 1.8 m above the ground (36.3 over 34.5) on dry land, with fog ExponentialSquared at density 0.0013. A retake with the same setup (a59-try1.png) is not grey haze: it shows a deer, but the camera is low, the foreground ground is blurred, the sky is overcast, and no cue is readable. a59-probe.png is a stray frame, not usable. Likely cause of the A-58b haze: the shot dir was missing (the editor clears Temp at startup), so those frames came from a different point in the run. Not checked.
- Row 2 (#64): network change: CreatureNet (the creature ghost) gets one new `[GhostField] byte Cue`. CreatureState.Cue (NeedCue: None/Tired/Hungry/Thirsty/Lonely, the strongest need past 0.6, none while asleep or held) is set by the brain in Needs, written by CreatureNetWriteSystem and copied back by CreatureReplicaSystem. The renderer draws state.Cue, so host, solo and client use the same path. **The host+client compare is not done**: the probe eval failed and I stopped at the ceiling. Fingerprint 51E641E9 after (host editor run, 0 game exceptions).
- Row 3 (#58): moved by reader (no before/after us/creature, not measured):

| Field | From | To | Readers |
|---|---|---|---|
| Hunger, Thirst, Tired, Lonely | CreatureState | CreatureMind | brain; FaunaDirector spawn; DebugControls -thirsty and hunt log; NetMenu means (host only, a client reads 0) |
| Cue (new) | - | CreatureState | brain writes; ghost; renderer |

  The spawn RNG draws keep their order, so spawns are unchanged. The raid spawn now edits CreatureMind instead of replacing it.

Shots: a59-try1.png (deer, no cue), a59-probe.png (not usable). Editor closed gracefully. Calls used: about 55 (ceiling 45).
Left: the host+client cue compare (Temp probe Cues(): ServerWorld vs ClientWorld by Individual), a framed `-needcues` shot (camera higher, nearer the herd), us/creature before/after for the split.

## A-60 (2026-11-04)

Commits: 57ad7b2 (animal-behaviours) – need cues drawn 3x bigger, plus a once-a-second `-needcues` log.
Fingerprint: 51E641E9 expected (render-only change); not re-measured this card.

**Row 1 (#64) – cause found.** The cues were drawn all along: they sit inside the drawn range, on the fear-mark path, but their radii were 0.2 to 2.6 cm, too small to see beyond a metre or two.
- Before: `[cues] creatures 97, brain cue != none 37, drawn range 3185; nearest 2044372579 at 8.7 m, cue 0: [0] at (-356.88,130.27,-46.99) len 0.005 r 0.023/0.023 a 0.45 drawn True`
- After (CueScale 3): `[cues] creatures 36, brain cue != none 3, drawn range 1249; nearest 2044372579 at 8.7 m, cue 0: [0] at (-356.64,131.06,-46.99) len 0.011 r 0.054/0.054 a 0.45 drawn True`
- "a" is the glow channel (Color.w); the marks have no real alpha.

**Row 2 (#64):** a60-needs.png. The camera is 2.8 m from creature 1362455765's head, and pale "tired" bubbles are visible above it. Takes a60-needs0 to a60-needs3 are also saved.

**Row 3 (#64):** host and client in one editor (`-host`): "host 118 creatures, client replicas 104, on both 104, same cue 104; host cued 41, client same 41". Examples: #A5482709, #5F7ABD0D and #62983A0F, each Thirsty on host and on client.

**Row 4 (#58):** not done (over budget). Plan: check out a49e354^ scripts, bench Brain.Tick per creature in the same scene, restore, bench again.

**Extra row (temple magenta), only partly done:**
- What it is: a flat blue-purple box, at the mushroom loot spot (3513,19,368) looking west, above the chest. It shows in a60-temple-probe.png, but a60-temple-mw.png is not taken yet.
- Probe results: viewport rays at (0.85,0.8) and (0.9,0.9) find no ECS render entity and no physics collider. The only GameObject renderer whose bounds they cross is "Dungeon SkullNiche" (ItemMaterial, URP/Lit) at 1.5 m, and it is small (0.26 x 0.60 x 0.42), so it is not the quad.
- What it is not: it is not the error shader (the colour is flat purple-blue, not magenta, and it has a lit edge). TempleGlow.mat is not implicated by the probe.
- Left: a mesh-level raycast against the cave/temple chunk meshes (they are not RenderMeshArray entities) to name it. My guess is a cave ceiling shaft showing the sky, or a temple decor slab.

Calls used: about 56 (the ceiling was about 45).

## A-61 (2026-11-04)

Commits: 3a09d58 (animal-behaviours, after merging origin/main): temple wall-decor stand-ins drawn stone grey and set flush into the wall (#24).
Fingerprint: 2DD98BAE (both the branch and main runs this card, and the A-60 session log too). It is not 51E641E9: main itself gives 2DD98BAE now, so the change came in with main, not this branch.

**Row 2 (#24), drawer named:** `DungeonSpawner.MakeProp`. It makes the placeholder cube for a WallDecor socket, content SkullNiche ("Dungeon SkullNiche", ItemMaterial), tinted through a property block with the hue from the content hash. For SkullNiche that is purple (0.57,0.41,0.75). From the mushroom spot the niche sits at (3513.4,20.9,368.5), 0.7 m from the camera, so a 0.40 x 0.60 x 0.12 box fills the upper right. A-60 ruled it out as too small, but it was just very close to the camera. Bisect: with all GameObject renderers off the box went (pixel (0.98,0.2) pink 0.95,0.77,0.84 -> wall 0.22,0.17,0.13). With only that one niche off it went too (0.24,0.18,0.14). Turning off SunkenTempleSystem, CreatureRender, SaltTower or Iceberg did not change it.
- What it's meant to be: a stand-in for a skull niche (wall decor) until set-piece art exists.
- Fix: wall decor now gets a low-saturation, dark tint (it measures 0.30,0.28,0.32) and moves half its depth back so it sits flush on the wall. Other props keep their hue tints.
- Shot: a61-temple-mw.png. The same pixel now reads 0.19,0.18,0.17. The editor camera at this spot sits closer than in A-57, so the framing differs.

**Row 3 (#58), only half done:** branch: 45 creatures, Brain.Tick 4.233 us/creature (0.190 ms/frame, 20 s, uncapped, at the temple spot). Main (origin/main scripts checked out, same spot): **0 creatures** were counted, so there is no number. Main's creatures had not spawned within ~60 s at the temple, or the CreatureMind query doesn't match on main. Next: bench both at a busy surface spot, waiting until the creature count matches.

Calls used: about 40.

## A-62 (2026-11-04)

Commits: dbfb2b7 (animal-behaviours, after a fast-forward to origin/main 7fbf224): DungeonPostSystem reads the NetPlayer positions in a server world (host and clients), falling back to PlayerTag solo, the same choice as CreatureBrainSystem (#24).
Fingerprint: not measured this card (it is editor only and the card ran out of calls before play mode). Main stays 2DD98BAE per A-61.

**Row 1 (#24), fix in, not yet shown on a host:** cause found: DungeonPostSystem queried PlayerTag, which a ServerWorld has none of, so on a host the guards saw no player at all. Dev build host runs on v2 (`-place temple -benchroute`, then `-goto 3519,19,360`): 0 exceptions, ServerWorld temple 0EBAB9D3 and both dungeons built, drink lines on the host (GlowMoth, CaveSalamander, GiantSpider), but still **no guard line** in either run. Not yet known whether the host's player actually stood within 7 m of a guard (the temple hash moved from 9C11B352 since 21c, so the chests may sit elsewhere). Next: a host run with a log of the nearest guard distance, or the profiler's DungeonCheck rerun on dbfb2b7.

**Row 2 (#58): not done** (ran out of calls). Note: after the merge the branch equals main (7fbf224), so the comparison is main before PR #117 (810955f) against 7fbf224, with the creature counts matched.

**Row 3, fingerprint shift: PR #114 (merge bf20ffb, campsites).** Read from the diffs, not bisected by runs: the fauna fingerprint hashes the flora blob, and FloraPlacement.Build collects its species from every landmark type's props, including biome looks (LandmarkBaking.AddLooks). #114 gave Campsite two biome looks (CampsiteForest, CampsiteSwamp) whose props are the new CampTentForest and CampTentSwamp species, so the flora species list, props and landmark type count in the blob all change. #113 (colossus keep-outs) only adds a runtime SharedStatic check in Place, not blob data; #112, #115 and #116 touch no baked data.

Calls used: about 50 (over the ceiling: two wasted on PowerShell quoting of the eval, two on host runs without a guard line).

## A-63 (2026-11-04)

Commits: f79d7f7 (merge of origin/main into animal-behaviours), ea4548d `-guardtest` (#24): puts this machine's player 3 m in front of a guarded chest once the dungeon sockets exist (solo/host: first guarded chest, joined client: the second), held 3 s. Test-only flag, off by default.
Fingerprint: not measured this card (the change is a test flag off by default; main stays 2DD98BAE).

**Row 1 (#24): done.** Dev build, v2, `-place temple -guardtest`, uncapped.
- Host solo: `[guardtest] ClientWorld (host): player put 3 m from guarded chest 1 of 3`, then `[dungeon] Tarantula guards its chest: faces a player 25 dm away, 0 dm from the chest`. 0 exceptions.
- Host + client (`-join 127.0.0.1`): host log has the Tarantula line for the host's player, then after the client connected `[dungeon] Goblin guards its chest: faces a player 42 dm away, 1 dm from the chest` (the client's player, put at guarded chest 2 of 3; client stood at 3 m from it). 0 exceptions on host and client. So the dbfb2b7 fix works on a server: the guards see the host's and the client's networked players.

**Row 2 (#58): half done.** Spot: `-place HumanVillage` (Sonoran HumanVillage), noon, `-perflog` shot list, 4 windows of 3 s after a 45 s settle. Branch (= main 7fbf224 + #24 commits): 98-99 creatures, brain ms 0.250 / 0.261 / 0.252 / 0.282 (mean 0.261), so **2.64 us/creature**; thinks/frame ~45, LOD skips/frame ~52. Main before #117 (810955f, the #117 merge's first parent; ccd2151 is an older merge): a temp worktree with a copied Library opened fine but `BuildPlayer` failed in Entities `ContentCatalogBuildUtility.BuildContentArchives` (status Error), so no number. Next: build 810955f from a clean import (or swap only the 8 #117 script files in place with the editor closed, minus files that use CreatureMind) and run the same shot list.

**Row 3 (#57): not started** (over budget). The HumanVillage brain ms above (0.261 ms, 99 creatures) is the "before" number to compare against once the stagger lands.

Calls used: about 50 (over the ceiling: two runs lost to log-path quoting and `-perflog` needing to be on each shot line, 15 min lost to a readiness poll that never connected).

## A-64 (2026-11-04): #57 stagger measured, #58 main-side number

Commits: none to code. animal-behaviours fast-forwarded to origin/main 6c39129 (PR #122) and pushed.
Fingerprint: 2DD98BAE on the branch and on 810955f (unchanged).

**Row 1 (#57): done, no code change needed.** The stagger is already in (A-45 3abc2c4, on main since PR #93): `SearchDue` runs FindPredator, FindAlarm and the asked-to-chat FindChat once per 0.2 s per creature, phased by a hash of Individual off `ElapsedTime` (the shared clock, so deterministic); a creature already fleeing keeps searching every frame, so reactions don't lag. "Keep the last result" isn't needed: a flee, once started, carries its threat; between slots no new flee starts, which is the 0.2 s delay the issue allows. `-oldsearch` per shot line is the A/B.
- Editor Play, uncapped, `-place HumanVillage -time 12`, 48 s settle, then 8 perflog windows alternating stagger / `-oldsearch` in pairs (ABBA-ish), 0 exceptions.
- Stagger: brain ms 0.424 / 0.401 / 0.407 / 0.452 (128-139 creatures), **3.17 us/creature**; predator searches/frame 2.3-2.6, alarm 3.5-3.7.
- Every frame (`-oldsearch`): 0.454 / 0.449 / 0.536 / 0.583 (131-147 creatures), **3.63 us/creature**; predator 41-43/frame, alarm 54-59.
- So about 13% less brain time per creature, 17x fewer searches. Chat searches were ~0 either way here. Not a bench townwalk run (no player build); the editor spot stands in.

**Row 2 (#58): done.** Same shot list, same spot, temp worktree of 810955f (main before #117) with a copied Library, editor Play. Removed after.
- 810955f stagger windows: 0.387 / 0.395 / 0.429 / 0.407 ms (124-138 creatures), **3.08 us/creature**; `-oldsearch` windows 0.420 / 0.429 / 0.486 / 0.478 (129-142), 3.33 us.
- Branch (6c39129, after #117): **3.17 us/creature** stagger, 3.63 `-oldsearch` (128-147).
- Branch vs main: +3% on the staggered windows (inside the window-to-window spread of about 10%), so #117's CreatureMind split costs nothing measurable. Counts match within 4 per window (creatures keep spawning in through the run, 124 -> 147).
- A-63's 2.64 us at 99 creatures was a different time of day (no `-time`), so compare only within this card.

Calls used: about 38. Editor closed (both); Temp/PlayArgs.txt removed.

## A-65 (2026-11-04): brain split, main-thread terrain samples
- Row 1 #61: no commit. The split already landed in A-47c (a172c51 + 2567e10): CreatureBrainSystem 1325 lines, .Activities 815, .Folk 907, .FolkWork 1037, .PetsCavesFlight 889, .Places 1073, .SensesMovement 772 (all LF, all under 1,500).
- Row 2 #63: e4880c2 on animal-behaviours.
  - Fixed: AmbienceSystem breathers (self conditions, footstep puff, remote players) re-sample ground/normal/surface only after a 0.5 m move. AmbientLife ear height re-samples after a 1 m move. CreatureRenderSystem swarm forager heights are cached per hive slot until the terrain changes.
  - Left: FaunaDirector (17 sites, all per event or rate-limited; changing them would move spawn RNG), Burst jobs, the OldCull debug path, UpdateSplashes (per event). Full table on #63.
- Editor HumanVillage `-time 12`, uncapped, 6 samples: cpu main mean 6.32 -> 5.88 ms (146-157 vs 131-136 creatures, so partly noise). Brain 0.37-0.67 -> 0.46-0.56 ms.
- Fingerprint 2DD98BAE unchanged. Both issues commented, left open.
- Calls used: about 50 (incl. pre-summary). Editor closed; Temp/PlayArgs.txt removed.

## A-66 (2026-11-04): fish folk and cave pygmies (#10, #35), Decide as a tier table (#60)

Commits on animal-behaviours: 3e2d774 (row 1, merged to main as PR #127 / 8502bca), then 69340e1 (row 2, on top of 8502bca), pushed.
Fauna fingerprint: 2DD98BAE -> D0B9488A (114 species) after row 1; row 2 keeps D0B9488A. Landmarks 108 / 3A0DC7BB, which is the game form of the editor's 4D2643D7: no change.

**Row 1 (#10, #35): done.**
- Fish folk now live in the existing FishFolk-flag settlements. There are 3 villages with 12 to 21 fish folk each.
  - They wade and swim in the shallows near home far more often than townsfolk (new `Wades` blob value).
  - They fish from the shore like other fishers.
- Blind cave pygmies live in the bat-folk chambers. There is 1 band of 6: PoolFisher 1, RoostKeeper 2, Tanner 1, Forager 2.
  - They are a ChamberFolk species, so the general cave-dweller spawner skips them.
  - New `Blind` flag: they sense by short-range touch, not sight, and carry a feeling staff.
  - The camp idea was dropped, as the coordinator asked. This closes the #35 gap that "no species walks yet".
- Shots (full size, not reviewed by anyone else):
  - D:\Unity\PunchyMilestones\animal\fishfolk-pygmies\gallery\a66-fishfolk.png
  - D:\Unity\PunchyMilestones\animal\fishfolk-pygmies\gallery\a66-pygmies.png
- cpu main at HumanVillage: 5.99 -> 6.19 ms (within noise). #10 commented, left open.

**Row 2 (#60): done.**
- Decide's early-return chain is now one table in tiers. Each forced or urgent entry is a small bool `Decide*` method. `Weights()` fills a FixedList indexed by the `Weighted` enum, `StartWeighted()` is a switch, and `WanderOff` is the fallback. There are no delegates, so Burst is unaffected.
- The order, top to bottom:

  | Tier | Entries |
  |---|---|
  | forced | Flit, Breach, Patrol, Hermit, Camper, (fisher spot fix-up), Rooted, Disguised |
  | senses | FindKin, FindPrey |
  | forced | Nest return |
  | urgent | Folk hunt 60%, Raid 8%, Play ring 30%, Hearth 80% |
  | (roll drawn) | |
  | forced | Greet, Roost, Burrow |
  | weighted | Work, Home, Hunt, Play, Sleep, Rest, Graze, then Drink*, Climb*, Swim*, Lounge*, Web*, Fly* (* = can fail; the spent roll falls to the next one with weight) |
  | fallback | Wander |

  The full table is on #60.
- Fingerprint is still D0B9488A.
- cpu main at HumanVillage, `-time 12`, uncapped, 3 windows each:
  - before 6.36 / 5.96 / 5.79 ms
  - after 6.31 / 6.71 / 6.98 ms (rose as creatures spawned in; within the A-64 window spread)
- Activity mix, share of creature-seconds:

  | Spot | Run | Creature-s | Idle | Graze | Rest | Sleep | Wander | Roost |
  |---|---|---|---|---|---|---|---|---|
  | HumanVillage | before | 7245 | 30.2 | 15.4 | 18.0 | 12.1 | 9.6 | 6.9 |
  | HumanVillage | after | 7169 | 30.8 | 15.9 | 15.2 | 12.2 | 12.6 | 5.7 |
  | Wild 900,-1400 | before | 4824 | 30.6 | 19.1 | 15.5 | 14.7 | 11.6 | 4.4 |
  | Wild 900,-1400 | after | 4936 | 30.5 | 19.9 | 13.8 | 15.5 | 13.9 | 1.5 |

  - Rest went down and Wander up at both spots. I checked the code line by line against the old chain.
  - The only real difference was the order of the nest return and `FindDoor`, which writes to the creature's state. It is restored in 69340e1 and only affects upright nesters with a home range (probably none).
  - So I'm calling the shift editor noise, but that is not proven. A second pair of runs would settle it.
- The runs were done before that fix and before merging 8502bca. The final commit compiled with 0 errors. #60 commented, left open.

**Follow-up: #60 differential proof.** For each creature in one play session, the old chain from 3e2d774 (temporary `DecideOld`) and the new table got copies of the same state, mind and `Random` seed, 300 seeds per creature.
- Mismatched picks were 0 at every spot: wild spot noon (100 creatures, 30,000 decisions), HumanVillage noon (106, 31,800), HumanVillage into night (104, 31,200) and the spot resets (253, 75,900). An earlier session was also 0 (428 creatures, 128,400 decisions). The state, rng and mind mismatch counts were also 0.
- No fix was needed, so 69340e1 stands with no new commit. The Rest/Wander shift was editor noise, and row 2 is safe to merge. The harness was not committed and has been removed, and Burst is back on. Posted on #60, which is left open.
Calls used: about 60 across the card (incl. pre-summary). Editor closed at the end.

## A-67: bat folk residents (#35)
Commit c0277c9 on `animal-behaviours`, pushed. It is on top of origin/main 6f139d0, which is merged locally.
- **Species:** a new BatFolk species on the folk rig (copied from the cave pygmy).
  - Big ears, as their own seeded style group: BigEar, PointyEar or GoblinEar, scaled 1.35 to 1.6.
  - A wing membrane on the arm.
  - Seeded size (0.62 to 0.8), 5 dark fur coats and 3 hair colours.
  - Each one holds a seeded staff, a stick or nothing. No lanterns.
- **Homes and work:** hammocks and goggle pegs are now posts. A hammock's sleeper is its roost keeper, followed by the hut, rack and basket posts.
- **New data fields:** `ShunsLight` and `SurfaceTrips`, plus the IdleAction `ShieldEyes` (an arm over the face).
- **New A-66 Decide entries:** a forced `Shield` entry and a weighted `Surface` entry (appended last, so the RNG order for other species is unchanged).
  - Light near means a player within 6 m under rock, or daylight on the surface.
  - Goggles: they wear goggles on the surface by day. Goggles stop the shielding.
- **Pygmies:** the band left the BatFolk chamber. It now lives in the largest chamber of a cave with no bat folk site, at (-3069, 159, 1017) on v3. It is seed-derived, one band.
- **Counts on v3:** 1 chamber with bat folk, 6 bat folk in total (PoolFisher 1, RoostKeeper 2, Forager 2, Tanner 1).
- **Fingerprint:** D0B9488A became 8726AADB (115 species). This is from the new species and the new blob fields.
- **Landmarks:** game form 108 / 3A0DC7BB (unchanged).
- **cpu main at the chamber** (`-place batfolk -time 23`, uncapped, editor): 3.23 ms before (76 creatures), 2.95 ms after (74). No measurable cost. Brain 0.43 to 0.49 ms.
- **Shots:** in D:\PunchyMilestones\animal\A-67\ (a67-batfolk.png, a67-batfolk-light.png).
- **Weak points:**
  - In the shots they read dark. The pygmy hair parts look like hats, and the ears are not obvious.
  - The hammock is not clearly in frame.
  - The light shot doesn't catch a shield pose. The player carries no lantern, so the trigger is distance only.
  - Fires and NPC torches don't count as light.
  - The surface walk is a straight line to the mouth.
  - Small eyes come from the head mesh as it is.
- **New debug flag:** `-place batfolk`.
- **Shot gotcha:** on `-island v3`, a shot list needs a `-wait 25` warmup line before `-place`.
- #35 commented, left open. Editor closed.
Calls used: about 110 (over budget).

## A-68 row 2, #35 bat folk look: done (verified)
- Commit d2a2bb3 on animal-behaviours, pushed. #35 commented.
- **Look:** hat caps gone (52 parts down to 31), ears 1.4x, coats about 1.35x lighter.
- **Light:** fires (hearth and camp anchors) and NPC torches or lanterns within 4 m count. A player within 6 m now counts underground. This was a bug: underRock is never set for cave dwellers, so the player's light used to be ignored at home.
- **Shield:** a light breaks off work straight into ShieldEyes, and alert drops while shielding.
- **Bug fix:** cave walkers that fled never stopped. The cave branch skipped Pause.
- **Verified on v3** (`-place batfolk`, player moved 3-5 m from a bat folk): ShieldEyes at 3.3-5.8 m in four samples. They return to Idle/Work afterwards.
- **Fingerprint:** 8726AADB became C1D6A1EA (BatFolk.asset only; brain changes don't feed it).
- **Shots:** D:\PunchyMilestones\animal\A-68\a68-batfolk.png (ears, hammock) and a68-batfolk-shield.png (arm across the face, side view).
- **Weak points:**
  - The shield shot is side-on and dim.
  - Close up, some still bolt after shielding (alarm cascade).
- **Next:** merge main f403b9c, run the play-mode suite, then row 3.

## A-68 merge + play-mode suite
- Merged origin/main f403b9c into animal-behaviours (8ba2385, plus cbf166d to fix the build). Pushed.
- **Suite** (scripts/run-playmode-tests.sh, editor closed, 231 s): 9 passed, 1 failed. The failure was Fauna_Fingerprint: it expected C1D6A1EA and read 2E01A16B, because main's species data is merged in. In 115678a the target is set to 2E01A16B, which the branch reads live now. The suite was not re-run after that fix.
- **Measured:** Any share 6.2% (worst HumanChild 13.0%); dialogue 0 unfilled; folk talk 13 lines, 0 through a wall; landmarks 108 / 4D2643D7; meadow fireflies 2.26, butterflies 0.29; river max 119.5, p99 103.4; village 4 creatures; world up in 44 s.

## A-68 row 3, #35 pygmies share the bat folk chamber: done (verified)
- Commit 9025e90 on animal-behaviours, pushed. #35 commented.
- **Placement:** A-67's separate PygmyChamber is gone. Each bat folk chamber now gets a pygmy band with its own seeds.
- **Shared chores:** blind cave dwellers below ground fish at the pool or tend near home. Bat folk carry an ore sack home on surface trips (Stage CarryGoods).
- **Bug fix:** underRock is never set for cave dwellers. The new `Below()` helper covers them. It fixes three things: pygmy chores never fired, bat folk wore goggles underground by day, and bat folk surface trips had never fired since A-67.
- **W-96 hooks:** the animal lane reads BatFolkPropKind `Pod` (RoostKeeper home, stand underneath) and `Niche` (pygmy home; stand = Position + mouth*(Size.y+0.5)) by name. Until W-96 lands the log says "not yet" and the lane falls back to the old homes.
- **Counts on v3:** 1 chamber. BatFolk: 6 (PoolFisher 1, RoostKeeper 2, Forager 2, Tanner 1). CavePygmy: 1 band, 3 in all, 0 at niches. By day, the pygmies work and tend within a few metres of the bat folk.
- **Fingerprint:** 2E01A16B, unchanged. This row only touches behaviour code, which doesn't feed the fingerprint.
- **Shot:** D:\PunchyMilestones\animal\A-68\a68-together.png. It shows the camp with pods and a hammock, a bat folk with a staff, and a pygmy working near the hammock.
- **Weak points:**
  - Two pygmies crept high up the chamber walls, one of them into rock. CaveFloorAt allows a 0.5 m step up every frame. This is older than this row. A possible fix is to scale the step-up by the horizontal distance moved.
  - The surface walk is still a straight line.
  - The sack only shows underground or at night.
  - The hammock stays until W-96 merges.
- **White circle:** I didn't see it in my editor, and my runs took no building-walk shots, so I couldn't reproduce it.
- Editor closed.

## A-69 row 1: flying in caves (done, verified) - 002b76e
- **Cause:** at the rock, flight stopped dead (and the mouth's open sky read as rock); every floor touch landed the bird and its takeoff (nose up) lifted it out of the mouth; the 2.2 rad/s turn was wider than the passages.
- **Fix:** slide along the rock's normal (then level, sink, back out); skim a cave floor while flying on; 4 rad/s turns in caves.
- **Check:** `-body Bat -flycave 3`, cave 0 (style 5) mouth to chamber node 4, 36 legs: 0 stuck, 0 landed, 0 legs timed out, 0 blocked frames.
- No shot (the row asks for a log).

## A-69 row 2: cave exit freeze (done, verified) - 26d3a46
- **Check:** `-body Bat -flycave 5`, 5 flights out of cave 0 with no stop: 0 stuck, 0 legs timed out, 0 blocked frames (2 floor touches walked on).
- **Frame times per exit (about 150 frames before and 180 after):** every window has a mean of 16.7 ms and 0 frames over 50 ms. The worst frame is 22.6 ms on exit 1, 39.2 ms on exit 2 and at most 23.8 ms on exits 3-5. The slowest speed is 2.7-4.9 m/s and SurfaceHidden is 0.00.
- **Fast climb out (30 m up, 60 m away, while chunks stream):** 5 of 5 climbs reached, 0 frames under 1 m/s, worst 41.7 ms, 0 over 50 ms.
- No shot (the row asks for a log). At default quality the brains and movement kept running, so no fix was needed in this row. The ultra plus crow repro is in the hand-back.

## A-69 control loss (Zach: crow, Ultra, fast climb out of a cave): not reproduced; probe + test - f372b82
- **Repro:** `-gfx Ultra -body Crow -flycave 5`: 0 stuck, 0 timeouts, 0 blocked frames; exits mean 16.7 ms, worst 24.7 ms, 0 over 50 ms; 5 of 5 climbs reached.
- **With hitches:** new `-flyhitch 700` stalls 700 ms every 45 frames on the climb. 3 climbs: worst frame about 718 ms, 0 frames off the wing, all reached.
- **Audit:** no fauna cull, sleep or disable pass matches the player (all exclude PlayerAvatar); no crow-specific system; no dt clamp or "no ground" cut-off in PlayerMovementSystem.
- **Test:** WorldTests `Player_CrowFliesOutOfCaveThroughHitches` (crow, cave 0 and out, climb through 700 ms hitches; asserts on the wing and reached).
- **Open guess (unverified, build only):** a long load hitch in a build may drop window focus; the Input System then resets held keys, so a held climb key stops until pressed again. Needs a build repro.

## A-69 row 3: mimic ocotillos (done, verified) - 6401071
- They do spawn in normal play, with no -spawn. The only biome that lists them is the Sonoran (density 0.2/ha, about 0.9 expected within 120 m). On v3 the Sonoran covers 1.64 km² in scattered patches, mostly around x 1570-3400, z -290 to -1950.
- In play with `-goto 2720,-736`: 1 mimic within 120 m about 30 s after the world was ready (at 2676,17,-812), alongside 92 other Sonoran animals. With `-place Sonoran` there were 0 mimics after 2 min. That flag lands at a HumanVillage in another biome, which may be how Zach missed them.
- Shot: Assets/Temp/shots/a69-mimic-wild.png. It shows the mimic disguised among real ocotillos and cacti.
- Not changed: ocotillo plants also grow in MesaCanyonlands (3.12 km² on v3), but no mimics live there. Adding the mimic to that biome's fauna list would roughly triple where it can be found. That is a biome asset edit (re-bake risk), so it waits for a decision.
- New hooks: TestHooks.BiomeArea(name) and TestHooks.SpeciesNear(name, radius).

## A-69 row 4: cave walkers climb walls (done, verified) - 2fbb4e3
- How far a creature can step up now depends on how far it moved: up to 0.5 m, as 0.01 + 1.5 x the distance walked that frame (CaveStep). The creature also snaps up to the floor only when it really stepped; otherwise it can rise at most 0.05 m.
- Bat folk and other cave walkers under the rock now follow the cave passage node by node to the mouth (PassageWay), instead of heading straight for the target.
- CaveFolk now puts bat folk at their BatFolkHomes pods and pygmies at their niches. `-place batfolk` already used those spots. It now keeps retrying until the homes exist; before this fix it fired too early and logged "no bat folk chamber with a pod". `-rockwatch` starts counting from that placement.
- Log for `-island v3 -place batfolk -rockwatch 300` (chamber 0 in cave 5, pod door at -2668,11,-601): `[rockwatch] DONE 301 checks over 300 s: up to 8 cave dwellers near, 0 in-rock samples, 0 creatures ever in rock`.
- No shot: the card asked for a logged count. Fingerprint: see the suite result in the hand-back.

## A-70 row 1: building solids baked once per settlement (done, verified) - c563b6c
- **What:** a new FootprintBakeJob runs on a worker before the brain. It places each settlement plot's building and each landmark's walled props once, when a player first comes near. `BuildingFootprints.Blocked` then reads those placements instead of re-placing the buildings (FloraPlacement.Place) on every check. The answers are identical: `-footprintcheck` found 0 disagreements in 916k checks across fv3, the village and fv1. `-oldfootprints` switches back to the old path for A/B runs.
- **Read stays in BrainJob:** the wall checks are interleaved with KeepOutOfSolids' step decisions, so they can't be pulled out into a separate pass without changing behaviour. Only the bake is separate.
- **Before/after, uncapped, ms per frame, two runs each:**

  | Spot | Brain.Built | Brain | Frame | BrainJob wall |
  |---|---|---|---|---|
  | fv3 | 2.45 / 2.50 -> 0.011 / 0.010 | 3.28 -> 0.80 | 8.27 -> 6.45 | 2.91 -> 0.45 |
  | meadow | 0.003 -> 0.003 | 0.98 -> 1.13 (noise) | 5.72 -> 5.64 | 0.40 -> 0.39 |

- **2 minutes at fv3 with `-solidstats`:** hidden >0.15 m went from about 9-12k ticks a minute to 0 / 0 ticks, max sink 0.00 m. Two cases caused the old count, and both were already there before this row:
  - A mouse or frog critter was drawn on a CritterSpot 0.3 m inside a house wall and sat there all visit. Fixed in AmbientLife.CritterSpots, which now skips mouse and frog spots that FloraPlacement.Blocked rejects.
  - A songbird resting 8 m up over a house footprint was counted as inside it. The stat now ignores creatures more than 2 m above the ground for building sinks.
- **World lane:** FloraPlacement.cs got small additive helpers (WallReachOf, InsideWalls), and Blocked itself behaves the same.
- **Fingerprint:** the critter-spot filter may move it. Suite result is in the hand-back. No shot (the row asks for numbers).

## A-70 row 2: brain as IJobParallelFor, batches of 8 (done, verified) - d7c147c
- BrainJob is now an IJobParallelFor over the thinkers query (the same set it ran on before: no player bodies, no test specimens). It takes one creature per index and gives workers 8 at a time. Each index writes only its own creature, through lookups. As an IJobEntity it was split by chunk, and with over a hundred creatures per chunk only about 4 workers ever got any work.
- **Uncapped, two runs each.** Before is row 1's after (same code apart from the scheduling):

  | Spot | BrainJob wall ms | Workers used | Spread (busiest / mean) | Frame ms |
  |---|---|---|---|---|
  | fv3 before | 0.453 / 0.353 | 4 / 4 | 2.19x / 1.73x | 6.45 / 6.43 |
  | fv3 after | 0.169 / 0.128 | 15.9 / 14.9 | 2.14x / 2.27x | 6.40 / 7.27 |
  | meadow before | 0.395 / 0.386 | 4 | 2.30x / 1.94x | 5.72 / 5.64 |
  | meadow after | 0.187 / 0.248 | 20.8 / 21.7 | 1.55x / 1.36x | 6.17 / 6.42 |

- Wall time is down 2-3x. The summed brain ms goes up a little (fv3 0.80 -> 0.75-1.12) because of lookup access and more workers starting up.
- Spread stays around 2x at fv3: the slowest batch holds the expensive near-player creatures. Frame time moves within noise, since the brain was not the critical path after row 1.
- Behaviour: suite and fingerprint are run before the hand-back. No shot.

## A-70 row 3: sync points (done, verified) - 940428b
- CreatureSnapshotSystem no longer copies on the main thread. It schedules one Burst CopyJob (states, bodies, entities) chained on the brain's handle, first in the presentation group.
- CreatureRenderSystem culls from this frame's copy behind that handle, so its own three copies are gone.
- The stale-tolerant readers (audio, breath, burrows, wakes, folk talk, traces and the mantraps, which now use the snapshot too) read last frame's finished copy (the double buffer), so they never wait.
- HeldPose skips the creature pass when nothing was held or carried in the last copy (a pickup flags it). When it does move anyone, the renderer copies its own that frame.
- `-syncsnapshot` restores the old path for A/B runs. `-perflog` now logs copies per frame and the main-thread brain wait.
- **Finding:** the 4.2 ms main-thread wait was already gone after rows 1 and 2. The brain job's wall time is now about 0.17 ms, so the wait is about 0.03 ms either way. What this row removes is the main-thread copying.
- **Uncapped, two runs each, sync (before) -> new (after):**

  | Spot | Frame ms | Copies/frame | Main-thread copy ms/frame | Brain wait ms/frame |
  |---|---|---|---|---|
  | fv3 | 7.27 / 7.41 -> 7.38 / 7.23 | 6.2 -> 3.0 (one job) | 0.015 -> 0.000 | 0.034 -> 0.032 |
  | meadow | 6.26 / 6.37 -> 6.40 / 6.34 | 6.2 -> 3.0 (one job) | 0.015 -> 0.000 | 0.033 -> 0.034 |

- Frame time stays within noise. In sync mode the old renderer copies plus Mantrap's are counted, so 6.2 is the "4 a frame" from the card as measured here.
- **Caveats:**
  - A pickup replicated to a client shows a frame later (it only appears in the next copy).
  - Stale readers can name entities gone since last frame. They only use them as keys or via Exists/HasComponent checks.
- No exceptions in either mode. Suite and fingerprint are run before the hand-back.

## A-70 row 4: pose rate (done, verified) - af84055
- Distance bands: under 40 m posed every frame, 40-100 m every 2nd frame, past 100 m every 4th, staggered by entity index. Held, puppet, haunting and lava creatures always pose.
- On a skipped frame the PoseJob carries last frame's body segments along with the creature's move and turn (one rotate + offset per segment) instead of running the animator. Grounding, fades, marks and held items still run every frame.
- Frustum cull, with a 6 m margin (plus 10% of SeenFrom) on the side planes. A creature carrying a light (lantern, torch) is never culled.
- Fewer capsule segments at distance: the mid band draws 8x3x2 capsules and the far band 6x2x1. Near creatures keep the full mesh.
- `-oldposerate` restores the old path for A/B runs. `-perflog` logs the pose job's median and worst ms, creatures posed per frame and creatures carried on per frame.
- **Uncapped, two runs each, old (before) -> new (after):**

  | Spot | Creatures | PoseJob median ms | PoseJob worst ms | Posed/frame | Frame ms |
  |---|---|---|---|---|---|
  | fv3 | 115-119 | 1.12 / 1.18 -> 0.25 / 0.30 | 2.07 / 2.23 -> 0.57 / 0.70 | 92 / 91 -> 15 / 21 (+9.5 carried on) | 6.63 / 6.60 -> 6.53 / 6.66 |
  | meadow | 178-212 | 0.46 / 0.49 -> 0.24 / 0.27 | 0.87 / 0.83 -> 0.39 / 0.56 | 55 / 53 -> 18 / 17 (+6-9 carried on) | 5.60 / 5.72 -> 5.88 / 5.85 |

- PoseJob ms is the job's own time summed over its workers. It drops 4x at fv3 and 2x in the meadow. Frame time stays within noise, because the pose job runs on workers off the critical path.
- One meadow "new" run had a single 29.8 ms max frame. Its median was normal and no exception was logged.
- Shot: fv3, the same spot with old and new side by side (row4-side.png, unviewed full res). The player, the deer about 15 m away and the birds look the same in both.
- No exceptions in either mode. Suite and fingerprint are run before the hand-back.

## A-70 row 5: Burst jobs for the per-creature reader loops (commit 61bf86b)
- CreatureAudio (HearJob, MoodJob), traces (DueJob), mantraps (NearJob) and wakes (SwimJob) now run their per-creature passes in Burst, in creature order. Sounds, marks and adds stay on the main thread, so the order and the budget are unchanged.
- -perflog now prints each reader's mean/worst ms per frame (ReaderTimes). Uncapped, two runs each:

| Reader | fv3 before | fv3 after | Meadow before | Meadow after |
|---|---|---|---|---|
| Audio | 0.102/0.395, 0.106/0.713 | 0.087/0.422, 0.091/0.434 | 0.104/0.295, 0.121/3.281 | 0.083/0.214, 0.083/0.238 |
| Trace | 0.091/1.239, 0.162/1.719 | 0.060/0.871, 0.073/0.981 | 0.053/0.936, 0.215/2.130 | 0.040/0.774, 0.089/1.166 |
| Mantrap | 0.085/0.153, 0.078/0.091 | 0.013/0.019, 0.015/0.047 | 0.117/0.151, 0.133/0.207 | 0.015/0.017, 0.016/0.017 |
| Wake | 0.083/1.483, 0.099/0.433 | 0.019/0.091, 0.023/0.063 | 0.026/0.207, 0.028/0.126 | 0.017/0.060, 0.017/0.152 |

- Frame ms (max in brackets): fv3 before 6.55 (8.1) and 6.59 (9.6), after 6.23 (8.3) and 6.21 (8.7). Meadow before 5.40 (6.8) and 5.97 (27.2), after 5.42 (6.6) and 5.29 (6.8).
- The suite found two problems, both fixed in this commit:
  - Last frame's snapshot could hand a disposed body blob to a Burst job after a body swap. The fix is CreatureSnapshotSystem.Forget(), called from FaunaDirector before blobs are disposed.
  - A reader's own fallback copy was Temp, which jobs can't take. It is now TempJob. FolkTalk also now releases its first copy (that was missing).
- The bench ran before these fixes. They only touch body swaps and fallback copies.
- Suite 11/11. Fingerprint 2E01A16B and landmarks 108 4D2643D7 are unchanged. No exceptions in either bench.

## A-70 extra row: mimic ocotillos in MesaCanyonlands (commit 9a870b3)
- OcotilloMimic is added to the MesaCanyonlands fauna list at density 0.2, the same as Sonoran. The rules are the same too: it waits curled beside a real ocotillo. The two-line asset edit is the only change.
- Suite 11/11 with the edit.
- Fingerprint 2E01A16B, unchanged. The fingerprint covers species builds (115 species), not biome fauna lists, so it doesn't move.
- Landmarks 108 4D2643D7, unchanged.
- No re-bake signal: the landmark hash and placement are the same. The editor bakes on every play start anyway, so I can't tell a forced re-bake apart from that.
- Shot (unviewed full res, local to the animal machine): mesa-mimic.png. It shows a mimic in Rest, curl 1.00, at (1943.6, -610.1), next to a lone real Ocotillo at (1946, -610), with Mesa red rock behind.

## A-71 row 1: comfort profiles and meter (commit 79a2d5a)
- New CreatureComfort.cs. Every species and race has a ComfortProfile:
  - A heat range, taken from its home biomes' climate.
  - How much rain, snow, storm, bright sun, wind and dry heat bother it.
  - A tolerance, and the shelter kinds it seeks.
  - Each individual gets a seeded tolerance (+-40%) and heat shift (+-0.05).
- Race tastes follow the card. Bat folk: Bright 1. Fish folk: Dry 1, and they like rain (-0.5). Humans: rain, storm and cold. Ogres: a warm range (0.42-0.90), so they feel the cold. Pygmies: Storm 1. Cats mind rain, frogs like it, big furry beasts pant earlier, birds mind wind and rain.
- Weather grid: 15x15 cells at 32 m round the players, built from WeatherField and biome climate. It is sampled on the main thread once a second (WeatherField is managed) and read in Burst.
- The brain checks each creature every 1-3 s, using a per-individual period and phase so checks are staggered. The meter builds by pressure*dt/30 and fades by dt/20. Underground or under rock, the sky terms are dropped and heat is mild.
- `-comfort` logs every 6 s: the meter per species, and each race's pressure at the player. `-comfort off` turns it off for the perf "before".
- Two fixes from the runs:
  - The log and grid timers survived between plays (domain reload is off), so the second play showed the last play's weather. Both now reset.
  - Rain now cancels dry heat (fish folk had complained of dryness in a storm).

Race pressure and kind at the player (pressure is 0..1):

| Race | Dunes noon (heat 1.00, sun 1.00) | Glacier midnight + SnowShowers (heat 0.17, snow 0.55) | Town 1 noon + forced Storm (rain, wind, storm 1.00) |
|---|---|---|---|
| Human | 0.77 Hot | 0.37 Cold | 0.72 Storm |
| Ogre | 0.37 Hot | 0.68 Cold | 0.32 Storm |
| BatFolk | 0.92 Bright | 0.20 Cold | 0.42 Storm |
| FishFolk | 1.00 Dry | 0.11 Snow | 0.22 Storm |
| Pygmy | 0.82 Hot | 0.42 Cold | 0.92 Storm |
| Dwarf | 0.97 Hot | 0.20 Cold | 0.42 Storm |
| Elf | 0.86 Hot | 0.42 Cold | 0.52 Storm |
| Goblin | 0.64 Hot | 0.42 Cold | 0.62 Storm |
| Hermit | 0.64 Hot | 0.20 Cold | 0.42 Storm |

- Animal meters:
  - Desert noon: they climb, for example Bat 0.17 to 0.51 in 16 s, Coyote 0.24, Gila monster 0.27, all Hot. Cave species stay at 0 underground.
  - Storm: Bat, Caribou, Crow, Ptarmigan and Wolf rise to 0.31-0.41 (Wind).
- Note: `-place Tundra` matches no landmark, so I used Glacier for the snowy night.
- No exceptions.

## A-71 row 2: shelter seeking (commit abc9669)

- Uncomfortable creatures (meter > 0.3; 8 searches a frame cap, 20-40 s retry) walk (hurry over pressure 0.6) to the nearest shelter that helps their complaint and that they like: door eaves or indoors (storm and cold always indoors), tree foot, well/pool/shore, hearth/camp fire (half-weighted distance when cold), cave mouth. Fliers roost. Wait facing out (animals lie down), leave when the open-sky pressure < 0.05 or after 90-240 s. Workers break off at meter > 0.55.
- Count, human village (2972,-885) in a forced storm at noon: sheltered at 30 s 6 (roof 5, tree 1), 13 on the way; at ~70 s 18 (roof 13, tree 5), 13 of them indoors. Humans' complaint there: Storm 0.61-0.72.
- Fish folk x8 spawned at DesertSpring (3531,-1122), 13:00, clear: kind Hot, 7 at the water within ~45 s. Shot: a71-fishfolk-desert (one standing in the spring).
- Ogres at OgreCamp, midnight, SnowShowers: the camp is warm (felt 0.52 > their 0.42 min), so only Snow (they took eaves/trees). With the debug cold snap -comfortheat -0.3 (forced, says so): kind Cold, 1 ogre at a fire, others indoors/eaves. Shot: a71-ogre-fire-snow (ogre with torch by the fire's glow, snowing).
- Humans in rain: shot a71-human-rain is weak (a Baker on the way to shelter in the plaza). That village is hot, so their complaint is Hot, not Rain.
- Bat folk at noon: not shot. They only exist in their underground chamber (cave 5), where sky terms don't apply, and -spawn puts cave dwellers on cave floors only. The profile (bright 1.0, seeks shade/caves/trees/roofs) is in place for when they come up.
- Not done: rock shadow and overhangs as shelter.
- Debug tool bug (pre-existing): -place NAME resolves against the first (v1) island's landmarks, so it lands in stale spots. Use -goto x,z.

## A-71 row 3: weather poses (commit a72e19f)

- New idle actions Pant, Shiver and Hunch, plus the existing ShieldEyes and a Shake on leaving the rain. Creatures that are uncomfortable but not sheltering (no shelter found, or one that can't shelter) strike the pose that fits the complaint: Hot/Dry pant, Cold/Snow shiver, Storm/Wind hunch, Bright shields eyes (folk) or squints. Folk and animals both pose; animals only make poses (and the existing sounds).
- Counts (comfort log, "posed N" is cumulative): human village, 13:00, heat +0.3: 6-8 posed in 46 s. Ice field (912,-3837), snow, cold snap -0.5 (forced): 8 posed. Village in a forced storm: 102 posed by about 120 s, with 12 sheltered and 34 shelter searches started.
- Shots: a71-pose-pant (a chicken panting by a bench in the plaza; it's seen from behind, so the open beak is hard to see), a71-pose-shiver (a cinder lizard shivering on a dead branch in snow; small in frame), a71-pose-hunch (a puppy hunched in the storm). Poses are motion, so stills only show part of them.
- Gotcha: adding a field to the ShelterCounts SharedStatic needed a new key and an editor restart. Burst kept the old size even after the key was renamed, so the restart was what fixed it.

## A-71 row 4: weather line bank (commit acc3177)

- Bank: `Assets/StreamingAssets/weather-lines.json`, 702 lines. Each of the 9 kinds (Hot, Dry, Cold, Rain, Snow, Storm, Bright, Wind, Relief) has 78 lines: 12 for anyone, plus 6 in each of 11 pools. The pools are 5 demeanors (Cheerful, Grumpy, Timid, Proud, Dramatic) and 6 races (Ogre, Goblin, BatFolk, FishFolk, Dwarf, Elf). Humans take the anyone and demeanor pools. Zach's seed lines are kept.
- When folk speak (`WeatherTalkSystem`, presentation, runs on every machine): by chance when setting off for shelter, ducking indoors, striking a weather pose, arriving (a Relief line), or now and then while waiting.
- Limits: one line per speaker per about 40 s; at most 2 lines up per 32 m cell (counting any folk line); 2 s between lines in a cell; nothing said through walls.
- Animals don't speak. They only pose. No new animal sounds were added.
- Test: storm at town (2972,-885) with `-comfort -weathertalk`, logged 22 lines. Every line was the only one up in its cell at the time (`[1 up in cell]`). The "most 2" in the log was left over from an earlier play, because domain reload is off; it is now reset in OnCreate. The log is below:
```
HumanChild (Human, Cheerful) indoors Storm cell (92,-29) [1 up in cell]: Wow, look at that flash!
HumanChild (Human, Grumpy) indoors Storm cell (93,-28) [1 up in cell]: Typical. Just typical.
Human (Human, Cheerful) indoors Storm cell (93,-29) [1 up in cell]: Woo! What a racket!
Human (Human, Cheerful) indoors Storm cell (93,-28) [1 up in cell]: Exciting, but let's go inside!
Elf (Elf, Dramatic) indoors Storm cell (92,-28) [1 up in cell]: The gods are angry!
Human (Human, Dramatic) seeking Storm cell (94,-28) [1 up in cell]: The sky is falling!
HumanChild (Human, Grumpy) indoors Storm cell (93,-29) [1 up in cell]: I've had enough of this sky.
Human (Human, Proud) arrived Relief cell (92,-28) [1 up in cell]: Comfort, as I deserve.
Human (Human, Cheerful) seeking Storm cell (93,-29) [1 up in cell]: Hold onto your hats, everyone!
HumanChild (Human, Grumpy) indoors Storm cell (92,-28) [1 up in cell]: Thunder. Just what I needed.
Human (Human, Cheerful) seeking Storm cell (93,-28) [1 up in cell]: Exciting, but let's go inside!
Human (Human, Cheerful) seeking Storm cell (92,-27) [1 up in cell]: Exciting, but let's go inside!
Elf (Elf, Dramatic) indoors Storm cell (92,-28) [1 up in cell]: The sky has lost its temper.
Human (Human, Proud) waiting Storm cell (92,-28) [1 up in cell]: Thunder? I have heard louder.
HumanChild (Human, Grumpy) seeking Storm cell (93,-28) [1 up in cell]: Storms. Always storms.
Human (Human, Cheerful) waiting Storm cell (93,-29) [1 up in cell]: Thunder! Count the seconds!
Human (Human, Dramatic) waiting Storm cell (94,-28) [1 up in cell]: The sky is falling!
Human (Human, Cheerful) waiting Storm cell (93,-28) [1 up in cell]: Oh, that's a bad one.
HumanChild (Human, Cheerful) seeking Storm cell (92,-29) [1 up in cell]: Exciting, but let's go inside!
Human (Human, Cheerful) waiting Storm cell (92,-28) [1 up in cell]: Big storm! Better find a roof!
HumanChild (Human, Grumpy) waiting Storm cell (93,-29) [1 up in cell]: Bah! Noise!
Human (Human, Dramatic) waiting Storm cell (94,-28) [1 up in cell]: Run for your lives!
```

## A-71 row 6: follow hook for lead quests, P-17 (commit ade09d7)
- API (host or solo; it goes through the server world): `Punchy.Fauna.Follow.Start(Entity, int playerNetworkId, float seconds = 0)` returns a bool, and `seconds` defaults to 600. Also `Stop(Entity)`, `IsFollowing(Entity)`, and `Following(Entity)`, which returns the player's networkId or -1. `Find(string species, float3 at, float radius)` returns the nearest free creature of that species, or Entity.Null. It skips ones that are gone, puppets, held, asleep or indoors.
- Behaviour: Greet stage 2. It walks once the player is >4 m ahead, runs when it is far behind, stops about 2.5 m plus half its body length short of them, and waits facing them while they stand. It gives up when the player leaves, gets >40 m ahead, or the time runs out, and also if the creature is rooted. It isn't threatened while following, it ignores its leash, and folk following a player don't take messenger errands.
- Test: FollowTests.Follow_ArrivesWithinFourMetres_StopReleases. `[measure] follow: dog 6.1 m off at the start, 4.7 m behind as the walk ended, 2.6 m after 6 s; following True, after Stop False`.
  - The walk goes along the flattest dry 30 m line (new FlatWay hook). The first spot walked up a 0.8 slope, the brain's steep-ground limit for every animal, and the dog crawled at 0.3 m/s.
  - TestSceneSetup.Spawn gained an optional radius.
- Card question, "do creatures flee a remote (client) player on the host?": yes. The host brain senses and flees from the nearest of all players' positions, including every GhostOwner player, not only the host's own.

## A-71 row 5: comfort + shelter seeking perf in a storm (no code change; measured on ade09d7)
- Run: one play session, `-island v3`, a shot list of 8 lines. Each line is `-time 12 -weather Storm -wait 30 -perflog`, alternating `-comfort off` and `-comfort` at the same spot. Brain ms comes from perf2, summed over workers over the last 3 s of each line, editor build, uncapped.
- Fishing village 3 (-4041,921):

  | Line | Comfort | Brain ms | Creatures |
  |---|---|---|---|
  | 1 | off | 0.330 | 83 (still spawning in, left out) |
  | 2 | on | 0.787 | 121 |
  | 3 | off | 0.954 | 137 |
  | 4 | on | 0.898 | 138 |

  Mean: on 0.84, off 0.95, so no measurable added cost; the creature count dominates.
- Busy town (2972,-885), about 100 creatures:

  | Line | Comfort | Brain ms |
  |---|---|---|
  | 5 | off | 0.485 |
  | 6 | on | 0.477 |
  | 7 | off | 0.455 |
  | 8 | on | 0.489 |

  Mean: on 0.483, off 0.470, so +0.013 ms.
- Result: the added cost is under 0.05 ms at both places, far below the 0.5 ms bar.
- Seen in the [comfort] log: in the town, folk went indoors (indoors 19, roof 10 at the end). At village 3, every shelter was a tree (roof 0) and "none found" kept climbing (20, then 42). Its huts gave the folk there no door to take.

## A-71 suite and fingerprint (0722b22)

- Suite 12/12 pass (217 s) on animal-behaviours with main 6d89525 merged in.
- Fauna fingerprint **EA6A4DE5** (was 2E01A16B; the comfort profiles). WorldTests retargeted.
- Landmarks 107 14D92F64: the new main baseline from W-98, not from A-71.
- FolkTalk WalkBy_NeverThroughWall failed once ("1 through a wall"). A real A-71 interaction, not flake: folk now head indoors out of rain/cold, sometimes right after a weather line, and the line rode them through the wall. Fix: lines already up drop once a kit wall comes between speaker and player (FolkTalkSystem). Rerun: 17 up, 0 through a wall, 805 held back.
- Follow test: dog 7.2 m off at start, 4.9 m behind at walk end, 2.7 m after 6 s.

## A-72 row 1: -place prop: waits for the flora placement (#137), c3879d2

- `-place prop:X` is now a pending request, tried each frame until the flora placement blob is built; warning
  "gave up, the flora placement never came up" after 60 s. `PlaceByProp` returns false while not ready.
- Verified: `-place prop:FishWharf` logs "2 FishWharf found in all", "framing FishWharf #1", shot a72-wharf after 31 s.
- Note for the world lane (asked by the coordinator): the A-71 FolkTalk wall drop (0722b22) changed world-lane W-97
  code in FolkTalkSystem.cs: a line already up is now dropped when a kit wall stands between its speaker and the
  player (`hasKit && Walled(...)` in the follow-speaker loop, which now runs backwards).
- Heads-up for rows 2-5: StoneColossi.cs (world lane, #40, W-58/W-89/W-92/W-94) already has WalkingSaguaro,
  RockGolem and GiantTortoise as static colossi. I'm doing row 6 next and will not edit StoneColossi without a
  go-ahead; see the hand-back for options.

## A-72 row 6: rock shelter (commit 36222a4)
- The comfort shelter search now counts big rocks: a boulder, tor, standing stone or hoodoo, at least the animal's height and 0.7 m through.
  - In wind, rain, snow or a storm, the animal goes to the downwind (lee) side.
  - In sun or heat, it goes to the side it came from.
  - A tall rock (over 2.5 m and over 1 m through) counts as an overhang, and the animal tucks in close.
  - In weather, a rock is worth a 25% longer walk than a tree.
- The search scans rings out to 7x7 tree-layer cells. A first try with a 24-cell sample (like FindTree) missed the one boulder in reach.
- The comfort grid now carries the wind direction. The grid's SharedStatic key is renamed (GridKeyV2) because its size changed; in one editor run, the old key hangs Burst.
- The `-comfort` log now has a `rock N` counter.
- Run: `-place prop:Boulder -time 13 -weather Storm -comfort -spawn Deer:8 -spawnradius 10 -wait 200`, one play session, 0 errors.
  - Deer meter 1.00, kind Wind; shelters: 7 started, 5 arrived, 3 at a rock.
  - The meter needs about 2 min of storm to pass 0.3, so a 60 s wait showed none.
- Shot: a72-rock-storm, a deer lying in the lee of a 2 m boulder in driving rain. It is partly hidden by the rock.
- No species or landmark change: the fingerprint and the landmark hash are unaffected. The suite runs before the hand-back.

## A-72 suite and hand-back
- Suite on 36222a4: 12 passed, 0 failed (251 s). Fingerprint EA6A4DE5 (unchanged), landmarks 107 14D92F64 (unchanged). FolkTalk: 21 up, 0 through a wall.
- Rows 1 and 6 are done. Rows 2-5 (colossus groundwork, walking saguaro, floating-rock golem, rock tortoise) are not started; they wait on a lane decision. StoneColossi.cs is world-lane code (#40), and it already places static WalkingSaguaro, RockGolem and GiantTortoise, which overlap these rows. Two options:
  - (a) The animal lane animates the world lane's colossi, with the world agent's OK to touch StoneColossi.
  - (b) The animal lane builds new walkers on the species, body and pose path (scaled up, from the ocean serpent species), and the world lane retires its static ones.
- My editor is closed.

## A-72 row 2: walking colossi groundwork (commit 53e688b)
- New animal-lane file `Fauna/ColossusWalker.cs` walks the GiantTortoise, RockGolem and WalkingSaguaro. The still giants stay still; the Mesa tortoise is not touched.
- Loop: a seeded wobbly circle (golem 38 m, saguaro 45 m, tortoise 40 m radius; 245-286 m round). Every check point is dry, in the spot's biome, slope at most 0.35 and clear of settlements by radius + 150 m + body. The loop shrinks x0.8 up to 4 times and may be nudged off the spot in 8 seeded directions (the golem and saguaro needed 2 shrinks and a nudge). If nothing fits, the giant stays still and logs why.
- Pace: walk then rest (golem 120/45 s, saguaro 100/50 s, tortoise 80/70 s) with 5 s ramps. Position is seed + `_PunchyClock`, so nothing is networked. Host and client places agree within 0.07 m (`-host -walkerlog`, 6 samples per walker).
- LOD: the walker nearest the camera within 600 m is awake (every frame, footfall sound StepRock pitch 0.3, 8-puff dust, camera shake within 120 m). The others update every 0.5 s. The far shadow re-lays after 10 m.
- Creatures give way: CreatureBrainSystem gets colossus zones (radius body + 6 m, x1.6 for fleeing species). A creature ahead runs off the path line, others away from the centre. 18 flees counted in one 60 s host run.
- Perf, one walker awake, near shot, uncapped editor: golem frame 16.7 ms (vsync), main 6.5, render 2.7, brain 0.225 ms. Saguaro main 6.2, render 2.2, brain 0.217 ms. Tortoise main 6.3, render 2.0, brain 0.471 ms (100 creatures). For comparison, the tortoise-far spot: main 5.8, render 1.6, brain 0.552 ms.
- StoneColossi.cs hooks (world lane, small, nothing else changed): (1) `public static float3[] Spots`, set at placement; (2) `public Transform Root(int slot)`; (3) `laidAt[]`, so a far shadow left 10 m behind is disposed and laid again. No placement, mesh or Salt change.
- World-data side effect: each walker's flora keep-out circle is widened once to the whole loop (radius x1.15 + body, 64-76 m), then Replant. This makes a trampled clearing.
- Debug: `-walkershot Kind,dist[,side|front|back]` frames a walker. `-walkerlog` logs every 10 s.
- Shots (scratchpad, unreviewed by Zach): saguaro-near/far, golem-near2 (front, 120 m), golem-far, tortoise-near/far2. The golem-near side view is blocked by its own spires.

## A-72 row 3: walking saguaro (commit 816d198)
- New animal-lane file `Fauna/ColossusRigs.cs`. Only the awake walker is posed. While it is awake, the saguaro's baked body is swapped for a skinned copy of the same mesh (149k verts). It switches back when the walker sleeps. No mesh, placement or Salt change.
- Bones come from the mesh itself:
  - The trunk axis is fitted to the mesh (47 m tall, leaning 6.5 m).
  - The arms are found by bearing clusters (2 arms).
  - There are 3 root-legs at 0/300/60 degrees.
  - The footprint trail is hidden while the saguaro walks.
- Motion:
  - The trunk sways with pace and footfall.
  - The arms swing like pendulums, with a little breeze sway at rest.
  - One root-leg lifts on each footfall, up to 16 degrees.
  - Dust emission is x(1+3·pace).
- Hook: ColossusWalker calls `Rig.Animate(pace, stride, settle, t)` and `Rig.Sleep()`. New debug arg: `-walkerphase walk|rest|seconds` puts every walker mid-walk or mid-rest, for shot lists.
- Shots (scratchpad a72/keep, unreviewed by Zach): a 3-frame side strip, saguaro-walk1/2/3 (60 m, 1.2 s apart). The root-legs are visibly in different places from frame to frame. Also saguaro-rest. 0 errors in the shot run.

## A-72 row 4: floating-rock golem (commit 816d198)
- `GolemRig` in ColossusRigs.cs. While walking, the golem's segments drift out from its middle by pace × drift. Drift is 1.2 m for the limbs, 0.6 m for the pelvis, 0.8 m for the torso, 1.6 m for the head and 3 m for the shards, with a slow wobble. Arms and legs swing in opposite phase, the feet lift and the chest rocks.
- At rest it closes up. The offsets are added after GolemArcs resets each segment every frame, so nothing builds up. No StoneColossi change.
- Shots (unreviewed by Zach), in the scratchpad (a72/keep and shots3):
  - golemb-walk1/2 and golemb-rest, from the back at 150 m. Walking it is a loose cloud of rocks with arcs between them; at rest it is a tighter body.
  - golem-walk1/2/3 and golem-rest, from the side at 220 m.

## A-72 row 5: FungalForest tortoise walks and settles (commit 816d198)
- `TortoiseRig` in ColossusRigs.cs uses a skinned copy of the body (7356 verts), swapped in only while the walker is awake. The Mesa tortoise and the still giants are not touched.
- Walking: the legs swing in diagonal pairs (12 degrees, 0.8 m lift), the shell rolls and the head nods.
- Settling: over the 6 s after a walk ends, the shell sinks 2.4 m and breathes, and the head lowers and looks slowly side to side. It rises again in the last 6 s of the rest. The mushrooms on the shell follow the shell.
- Shots (scratchpad shots3, unreviewed by Zach): tortoiseb-walk (side, 80 m) and tortoisec-rest (back, 90 m, sunk low). tortoiseb-rest is partly hidden by giant mushrooms.

## A-72 rows 2-5 suite and hand-back
- Suite on 816d198: 12 passed, 0 failed (243 s). Fingerprint EA6A4DE5 (unchanged); landmarks 107 14D92F64 (unchanged). FolkTalk: 16 lines up, 0 through a wall.
- All six A-72 rows are done. The Mesa tortoise is left for later; the still giants stay still.
- Work outside the animal files:
  - StoneColossi.cs hooks from row 2 (Spots, Root(slot), laidAt).
  - The CreatureBrainSystem colossus flee zones.
  - The flora keep-out widened to each loop.
- The rigs are all in `Fauna/ColossusRigs.cs`. New debug arg: `-walkerphase`.
- My editor is closed.

## A-72 merge of main (#148, #149, #150) into animal-behaviours (77bd6b3)
- Merge 8d82fa9 brought in #148 and #149; merge 77bd6b3 brought in #150.
- DebugControls.cs conflict: both sides had fixed #137 (`-place prop:` waits for the flora lookup). I kept main's `PlacePropPending` and dropped the branch's copy of the retry. `-walkershot` and `-walkerphase` are kept. The file is still LF.
- Suite on 77bd6b3, with my editor closed: 13 passed, 0 failed (263 s). Both baselines match main's:
  - Fauna_Fingerprint: 1202B8ED
  - Landmarks_V3: 109 C96F266F
- The walkers still walk on main's terrain:
  - Golem loop: 245 m. Saguaro loop: 286 m. Tortoise loop: 255 m.
  - The rigs build, and the shot merge-golem2 shows the golem walking with its rocks drifting.
- **The walkers fight ColossusCollision (not fixed, as asked).**
  - Cause: `ColossusCollision.Register` samples each colossus into fixed world-space columns once, when StoneColossi builds it, before the walkers start. The columns never follow the root.
  - In play, the column centres stay at their build spots while the roots move:

    | Walker | Column centre (build spot) | Root (live) | Root off the column centre |
    |---|---|---|---|
    | Golem | (705, -328) | (706, -364) | about 36 m (reach 34 m) |
    | Saguaro | (2937, -1697) | (2867, -1675) | about 73 m (reach 53 m) |
    | Tortoise | (-1503, -2767) | (-1531, -2748) | about 34 m (reach 44 m) |

  - Effect: an invisible solid footprint stays at each build spot, the walking body has no collision for most of its loop, and ColossusPushSystem pushes creatures out of empty air.
  - Possible fix: store the columns relative to the root and transform them by the root's pose each frame, or re-register while walking. This needs a world-lane decision, since ColossusCollision.cs is world-lane code.
- My editor is closed.

## A-73 row 1: Village_CreatureDensity deterministic (b42fd25)
- Cause: `-place HumanVillage` stood the player at a stale village 98 m from the centre (DebugControls never cleared `centres`), so the count swung with whichever village it was.
- Fix: clear `centres` with `locations`; test places at `HumanVillage:centre`. Band re-baselined to 8-40 creatures (was up to 20) and a new folk floor of 4, so it checks more than before, not less.
- 5 suite runs in a row, all 13/13 passed: 25/11, 28/13, 29/16, 26/14, 28/15 (creatures/folk). Fingerprint 1202B8ED, landmarks 109 C96F266F.

## A-73 rows 2-4: hut doors, pier work, wharf gulls and fish (8222749, repin 6afa951)
Rows 2-4 share one commit because their files overlap (FaunaDirector, WharfSpots, brain).
- Row 2: at night and in rain, fishing folk head for their own hut's door (Shelter weighting). They never seek shelter while they're on the pier. Shot door-a: at dusk in rain, folk walk in by the stilt-hut steps.
- Row 3: WharfSpots reads the end, loop, nets and crate spots from the wharf model. 48 spots and 190 rail perches across the island. Folk walk the deck, fish off the end or loop with a rod, mend nets, and carry crates (drawn as a wooden crate). Shots fish-a (folk at the crates on the deck) and wharf-wide.
- Row 4: seeded gulls (new Gull species) sit on half of the wharf rail and post perches, 38 spots. They're critter-tier creatures that flee and take off within 7 m of the player. A new Mackerel species gives 1-2 leaping fish 4-9 m off each pier end, in water at least 1.2 m deep. Shot gulls-b (gulls on the rails and posts). Perf at the fishing wharf: render 1.76 ms, cpu main 6.35 ms, gpu 0.22 ms, 155 creatures.
- Fauna_Fingerprint 1202B8ED -> 773CA7F6, because Gull and Mackerel joined the CritterTable (it feeds AddSpecies). The test is repinned. Landmarks are still 109 C96F266F. Suite 13/13 passed (209 s).
- Ask for the world lane: buoy masts aren't in the wharf model Boxes, so gulls can't perch on them yet. Please export them as perches.
- Main has moved since my merge (PR #152, W-105). I haven't re-merged.

## A-73 merge (origin/main PR #152)
- Merge a08fa3d on animal-behaviours, pushed. WorldTests.cs conflict resolved to main's landmark pin 110 09EAC0FE.
- Fauna_Fingerprint repinned to F4E66093 with comment: W-105 stilt houses (main alone 61459BF0) plus A-73 Gull and Mackerel (A-73 alone 773CA7F6).
- Suite: 13 passed, 0 failed in 206 s (fingerprint F4E66093, landmarks 110 09EAC0FE, wharf spots 48, gull perches 190).
- Doors: NOT verified after the merge. Dusk rain shot door-m1 shows the stilt walkway and steps intact, but no folk in frame; -place prop:FishWharf now picks wharf #1 (390, -619), away from village folk, and -place FishingVillage:centre no longer moves the player. door-m2 camera ended inside geometry. Pre-merge door-a showed folk at stilt-hut steps. Needs a framed shot at a village wharf.

## A-74 rows 1-2 (animal-behaviours c17e66f)
- Row 1 done. Cause: launch options run on the baked first island; fishing villages (and every other type the v2/v3 pass appends) only exist after that pass, so the generic `-place` found nothing and left the player where they were. Fix in DebugControls: a `-place` that finds nothing retries each time the landmark key changes, for up to 120 s, then logs a warning. Verified: "started at FishingVillage:centre", player at the village (shot fv-over shows the stilt houses). Covers every `<Landmark>` / `<Landmark>:centre` target that is v2/v3-only; baked types still place on the first try. Also added `-pickspecies <Body>` for the row 3 shots (compiles clean).
- Row 2 done, no code change needed. At 20.3 h in rain at the fishing village, 8 folk are in Home mode within 150 m: 7 of them are within 0.7-2.1 m of their door anchor. Shot fe-door2: a villager at the foot of a stilt hut's stairs, lit. (fe-door1/fe-door3 show huts with no one outside; the ones at 0.7 m are on the deck.) Night shots at 21.7 h (fd-*) were too dark to read.

## A-74 row 4 (#40): colossus extras (animal-behaviours 8a91273)
- Dust and ground shake only play within 300 m of the player.
- A walking colossus within 40 m of the player holds its step (2 s ease), turns its root up to 70 deg and its head up to 35 deg toward the player, then catches up on its loop at 1.5x pace. If the player stays close for more than 30 s, it gets bored and walks on.
- Cosmetic and local per client: each client's walker reacts to its own player, and the loop clock doesn't drift, because the lag is paid back. `-walkerlog` prints hold / lag / look / bored.
- Strip wstrip (w-far, w-0, w-1, w-2): the walking saguaro at 45 m side-on stops and turns to look at the player. Log "hold 0.21 look 8" when it's in range, "hold 0.00 look 0" when it's far.
- Brain ms with the walker awake: 0.269 at w-far and 0.303 at w-2.
- ColossusCollision.cs and StoneColossi.cs were not touched.

## A-74 row 3 (#8): NPC reactions to the player's race (animal-behaviours 485cb4e)
- #8 was already built in Z-52. Each species has a stance table toward the player's body, picked by seed (Kin / Neutral / Wary / Annoyed / Rival). A player emote within 20 m gets an answer: Wary folk ignore it, Annoyed folk walk off 5-8 m, Kin and Warm folk wave back, Rival folk give a mock bow. Without an emote, a Wary one within 3 m steps back to 4.5 m, and Kin, Warm and Rival folk sometimes wave at 1.5-6 m. FolkTalk walk-by lines match the stance. It's simulated state: brain and FolkTalk run on the host, and clients see the emote and pose. `-stancelog` prints the tables.
- Code this row (debug only, 485cb4e): `-place` places again when the v2/v3 pass moves landmarks inside its 120 s window. Before, HumanVillage:centre could land at a stale baked spot on a bare hillside (settled folk 0/60); now it lands at the real village (7 m away, 28/60). `-goto` / `-at` / `-walkershot` cancel a pending re-place. `-pickspecies` waits for the player to exist. Caveat: `-weather` applies at the player's position, so put it on a later shot line.
- Shots at ElfGrove:centre, 9 h, clear, 21/60 settled. Strip s21 (e-hum1, e-hum2, e-elf1, e-elf2).
  - Wary, Human body (Elf stance toward Human: Neutral 48 / Wary 32 / Annoyed 20): log "Elf (Wary) to a Human: Humans cut trees. We remember." The Neutral ones answer the wave politely ("Be welcome.") and don't wave back.
  - Friendly, Elf body (Kin): e-elf2 shows an elf waving back with the line "Ah, family. Welcome."
- Why not a human village: folk at the desert HumanVillage (2979, -887) stay in Shelter/Home almost all day because of heat comfort, even at 7-8 h in clear weather (run16: 8 of 9 near folk in Shelter/Home). Reactions only run in Free/Work modes, so the shots were taken at ElfGrove. Possible follow-up: is desert sheltering too strong?
- Brain ms: 0.914 (Human body) and 1.091 (Elf body) at the grove, and 0.42 / 0.47 at the desert village (96 creatures). The brain code didn't change this row, so brain cost is unchanged. The numbers differ between sites only because of creature counts.

## A-74 done: suite
- Suite on animal-behaviours 485cb4e: 13 passed, 0 failed in 257 s (Unity exit 0).
- Fauna_Fingerprint F4E66093, unchanged: no species or species assets changed in A-74. Landmarks 110 09EAC0FE.
- A-74 commits: c17e66f (rows 1-2), 8a91273 (row 4, #40), 485cb4e (row 3, #8). All pushed. The Punchy editor is closed. No PRs merged and no issues closed.

## A-75 row 1 (desert folk shelter only at midday) - done (verified)

- Commit 70eca71 on animal-behaviours. Comfort learns the hour (a shared static fed by FaunaDirector). On hot islands the felt heat eases off away from about 11-16 h. At midday a seeded hardy half of the folk fan themselves and carry on. Of those who shelter from Hot/Dry/Bright, 85% stand under the eaves, and 15% go indoors (it was 50%). Rain is unchanged. #69 hermit and settlement code are untouched.
- Desert HumanVillage, folk within 70 m (indoors / eaves shelter / outdoors):
  - before, 8 h: 16 folk, 5 / 10 / 1
  - before, 13 h: 15 folk, 4 / 9 / 2
  - after, 8.8 h: 17 folk, 2 / 0 / 15 (heat meter 0.03)
  - after, 13.8 h: 15 folk, 3 / 6 / 6 (meter 1.00; all feel Hot)
- Snow village (tundra), after:
  - It had the same fault: at 14.6 h before the hardy rule, 2 of 15 were out (Bright/Hot glare).
  - after, 13.8 h: 14 folk, 5 / 0 / 9
  - after, 8.8 h: 14 folk, 4 / 2 / 8
- Ash village (the island has no ElectricWastelands settlement), after, 13.8 h: 12 folk, 4 / 1 / 7. It was already fine before (6 of 11 out).
- Shots: d8, d13 (after); base-d8 and base-d13 are the old code. The snow shots are s8 and s13, and the ash shot is a13.
- Brain ms in the desert village at 13 h: 0.617 after, 0.604 before.
- Risk: the hour is a global static, so on a joined client it is that client's own clock (it's cosmetic behaviour only).

## A-75 row 2 (#40 the mesa tortoise walks) - done (verified)

- Commit on animal-behaviours: the ColossusWalker gait MesaTortoise (slot 8), a MesaTortoiseRig in ColossusRigs, the A-74 look-at-player head and in-range dust. StoneColossi.cs needed no change (KeepOutLast already covers slot 8).
- The canyon ground near its spot fits no loop (the first try said "slope 2.03"). So Lay() gains a last-try search 1.2-2.4 radii farther out; the walkers that already fit never reach it. Log: "MesaTortoise walks a 147 m loop (radius 23 m, 8 shrinks) round (2064, 1327), 0.5 m/s, 90 s walking then 80 s resting; flora kept 52 m out".
- Shots: the walking strip is m-0, m-1 and m-2, from 110 m at the side with the eye 45 m up (`-walkershot` takes an optional 4th part, the eye lift). m-far is the setup shot and m-look is from 45 m in front. In m-look the head is low right and the shell fills the frame, so it's tight; a wider look shot can follow if wanted.
- Brain ms with it awake: 1.043 (m-far) and 0.910 (m-look), with 106-114 creatures. The desert village gives 0.617 for comparison. The extra cost is the canyon fauna (more creatures and flee searches), not the walker, which runs on the main thread outside the brain.
- Fauna_Fingerprint: reported at the suite run before hand-back.

## A-75 row 3 (#40 colossus per biome) - done (written only, no code)

Each colossus stands in the first matching biome region that StoneColossi's survey finds (one per island, seeded). The Dunes giant has its own placer (DunesColossus).

| Biome | Colossus | Moves? |
|---|---|---|
| Tundra / Glacier / Mountains | Kneeling titan (one, in whichever of the three it finds) | Still |
| Grassland / Temperate | Half-buried head | Still |
| FungalForest | Giant tortoise | Walks (A-72) |
| ElectricWastelands | Rock golem | Walks (A-72) |
| PoisonSwamp | Carnivorous plant | Still |
| Ashlands | Skeletal lizard | Still |
| Sonoran | Walking saguaro | Walks (A-72) |
| SaltFlat | Salt crystal | Rolls in slow circles (W-92) |
| MesaCanyonlands | Mesa tortoise | Walks (A-75 row 2) |
| Dunes | Half-buried humanoid, arm drags a furrow in a sandstorm | Idles in place |
| FoggyMarsh | None (the drowned knight is built in code; `-colossus` swaps it in for the plant) | - |
| EmberForest, Volcano | None (the ash smith is built in code; `-colossus` swaps it in for the lizard) | - |
| GlassDesert | None | - |
| SparklingForest | None | - |
| Ocean | None | - |

The gaps are GlassDesert, SparklingForest, EmberForest/Volcano and FoggyMarsh. Two of those already have built but unplaced kinds (DrownedKnight, AshSmith; CrystalSerpent is unused too), so the cheapest wins are there. My two proposals for the next giants are below.

1. **Glass heron (GlassDesert).**
   - Look: a 45 m wading bird of fused glass plates on two stilt legs. It's see-through, so the sun casts coloured light and sparkles through it.
   - Behaviour: it stalks a slow seeded loop like the walkers. Every few minutes it stops, dips its beak into the sand and lifts it with a glitter puff. It is a walker with a one-bone dip.
   - Cost: low to medium. It uses the ColossusWalker gait and rig pattern, plus one tinted-glass material. No new systems are needed. About one card row, plus a world-lane slot and salt in StoneColossi (adding a slot does not move the existing landmarks).

2. **Lantern stag (SparklingForest).**
   - Look: a 40 m stag of mossy bark. Its antlers are hung with glowing seed pods that light the canopy at night.
   - Behaviour: still by day, lying down, with its head turning. At dusk it rises, walks its loop, and sheds a few drifting light motes (particle) that small creatures gather round.
   - Cost: medium. It is a walker with a day/night state (it reads the same clock that A-75 row 1 feeds to comfort) and an emissive pod material. The rig is about two times the tortoise's, because of the four long legs and the neck.

Runner-up: give the DrownedKnight its own slot in FoggyMarsh, rising slowly from the water at dusk. It is the cheapest, because BuildKnight already exists, but a new slot is placement work, so it needs the world lane and a warning first.

## A-75 extra (gulls on the wharf buoy masts) - done (verified)

- Merged origin/main into animal-behaviours (4c6bded). It brings #158, #159 and the PlayerPlaced fix in DebugControls, taken as is. No TestHooks calls remain outside Editor/ and Tests/, and the scripts compile clean in the editor.
- Commit 27647f1. The WharfSpots perch filter now also accepts a short post that starts above -0.3 and tops out under Deck + 0.2 (the buoy masts, bottom about 0.15). Piles reach deep below and stay out. The startup log now says "gulls 61 (7 on buoy masts)".
- Shot: g-mast2, a gull on a buoy mast at (408.6, 1.2, -605.2).
- Risk for the world lane: in that shot the buoy floats right beside a stilt house's piles. The buoys "off the far end" can land under neighbouring stilt houses.
- Suite: 13 passed, 0 failed. Fauna_Fingerprint F4E66093, unchanged: the A-75 changes are behaviour, a walker and spot code, with no species or creature data. Landmarks 110 09EAC0FE, unchanged.

## A-76 rows 1 and 2 (glass heron, lantern stag) - done (verified)

- Commit 295681a on animal-behaviours. Merged origin/main afterwards as bfb1393 (brings #162), then pushed. Only three files changed: StoneColossi.cs, ColossusWalker.cs and ColossusRigs.cs, all LF.
- New StoneColossi slots: 9 GlassHeron (salt 0x6E70) and 10 LanternStag (salt 0x57A6). The nine existing colossi are at the same positions before and after.

| Slot | Position | Facing | Region | Relief | Nearest town |
|---|---|---|---|---|---|
| GlassHeron | (2629, 10.6, 2034) | 68 | 24 cells | 3.8 m | settlement 1 (type 3), 2940 m |
| LanternStag | (-2637, 110.1, -320) | 51 | 138 cells | 5.3 m | settlement 5 (type 20), 1180 m |

- Walkers use the ColossusWalker spot and root hooks, walker collision, look-at-player and in-range dust.

| Walker | Loop | Radius | Flora kept out |
|---|---|---|---|
| Heron | 287 m | 45 m | 62 m |
| Stag | 415 m | 65 m | 89 m |

- ColossusZones now hold the four nearest moving walkers, not the first four.
- HeronRig (10830 verts): wades with long rests and stabs at the sand every 9 s while resting. A new walker phase, "stab", holds the stab for shots.
- StagRig (19440 verts): grazes while resting. The antler lanterns are FireSystem flames plus CaveSystem moving lights, faint by day and brighter at night (DayNightCycle.Night). They are cosmetic and local.

Perf with each one awake (180-frame -perflog):

| Shot | Brain ms | Frame ms | Main ms | Render ms | GPU ms | Creatures |
|---|---|---|---|---|---|---|
| h-far | 0.218 | 16.79 | 6.64 | 2.46 | 0.21 | 47 |
| h-look | 0.231 | 17.03 | 6.48 | 1.75 | 0.23 | 54 |
| s-day | 1.303 | 16.77 | 6.52 | 1.90 | 0.18 | 134 |
| s-night | 1.450 | 16.77 | 6.72 | 1.83 | 0.19 | 131 |

- Shots: h-0/h-1/h-2 (walk strip), h-stab, h-look, h-far; s-0/s-1/s-2 (walk strip), s-day, s-night, s-look. Contact sheet: sheet.png.
- Weak points:
  - Both walk strips are 1.5 s apart, so the motion is subtle; the stag's legs barely change between frames.
  - At night the lanterns read as small warm points from that shot's distance (about 60 m). A brighter flame or a closer night shot would sell it better.
- WorldTests suite: 9 passed, 0 failed.
  - Landmarks 110 09EAC0FE, unchanged.
  - Fauna_Fingerprint F4E66093, unchanged. The colossi are StoneColossi slots, not fauna species, so no species or creature data changed.
- The landmark bake log's own hash (CE5EB4D9) is the same as in earlier logs. It is a different measure from the test's probe hash.
## A-77 row 1 (items 19, 20) - 419554b on animal-behaviours - PARTLY VERIFIED
- Code: creatures steer round colossi, both static and walking. ColossusPushSystem fills a ColossusSnapshot singleton that the Burst brain job reads, one frame late. Steer has a new AroundColossi step: tangent round a walker's circle, slide off a static column, and wanderers pick a new goal. A SharedStatic switch (ColossusSteerOff) gives a before/after toggle. Pygmies and bat folk (CaveDweller) get no hat and no guard kit. Compiles clean.
- Brain ms: 0.357 (60 creatures), 0.607 (104), 0.687 (105). Same range as before.
- Stuck-at-colossus count: NOT MEASURED. The 2 min probe at the golem and saguaro read 0 near / 0 stuck both off and on, because no creatures were near the walkers. A rerun needs creatures spawned inside the walk loop (-spawn ... -spawnradius 30).
- Bareheaded shot: FAILED. folk-a framed a boulder at a cave mouth with no folk in view. Needs a retake with the folk spawned in front of the camera.
- Row 2 (plant, heron, saguaro, golem) not started. Out of call budget. The plan is in the hand-back.
## A-77 row 1 (items 19, 20) - 419554b on animal-behaviours - retest on v3 - STUCK COUNT MEASURED, FOLK SHOT STILL MISSING
- Retest on the v3 island (`-island v3` in the shot list), at 13:00, with deer, rabbits and dogs spawned on each walker's loop ring.
- Stuck-at-colossus count, 2 min each (12 samples 10 s apart):
  - Rock golem, steering off: 88 creatures, 2 within 10 m, 1 touching, 0 stuck 30 s+.
  - Rock golem, steering on: 113 creatures, 11 within 10 m, 8 touching, 0 stuck.
  - Walking saguaro, steering off: 93 creatures, 2 within 10 m, 1 touching, 0 stuck.
  - Walking saguaro, steering on: 97 creatures, 5 within 10 m, 1 touching, 0 stuck.
- Why 0 with steering off: creatures do reach the ring, but very few, and none stays. The walker moves on, and the existing push (ColossusPushSystem) shoves them clear before 30 s. So the count can't show a before/after on this island. The steering is a quality fix (no walking into the legs), not a fix for a measured stuck count.
- Brain ms: 0.15 to 0.83 with 88 to 113 creatures. Same range as before.
- Bareheaded shot: NOT OBTAINED after 3 tries.
  - `-goto 0,0 -incave` left the player on the snow surface.
  - `-place batfolk` logs "at bat folk chamber 0, pod door at (66, 34, 3436), pod floor at y 37.1". But the shot shows open forest, and the spawns log "spawned 0 CavePygmy / 0 BatFolk on the cave floor".
  - On v3, either the player isn't kept in the chamber, or that chamber has no floor within 2 m. This needs a look from the world lane or a fixed cave spot. The code change (CaveDweller races skip the hat and guard kit) compiles and is unchanged.
## A-77 row 2 (items 21-24) - fdfa58a on animal-behaviours - VERIFIED (shots on v3)
- Poison plant colossus (CarnivorousPlant at (-53, 34, 516)): 2x tall and animated. The head, jaws and tendrils sway on PlantRig/SwayPlant. In plant-1 and plant-3 (5 s apart) the head tilts and the jaws and tendrils sit in different places.
- Glass heron: the crystal litter is laid unparented, so it no longer swivels with the bird. qlit at t 610.8 s and t 650.8 s: litter at (2629.0, 10.2, 2034.3), no parent, both times.
  - New this round: ColossusWalker hides the litter whenever DunesColossus.Hidden is set, and destroys it when the colossi are rebuilt. Capture then lays it again.
  - Caveat: the heron stood at the same spot at both samples, so this run doesn't show the bird moving away from the litter. The a77r2 run had already shown the litter staying put.
- Walking saguaro: the legs splay at 180/-60/60 with root parts, and from the side they read as a root flare.
- Rock golem: black basalt colours. It reads dark brown-black next to the white lightning.
- Render ms: 1.47 to 2.15 (gpu 0.17 to 0.24) across the shots.
- WorldTests suite after fdfa58a: 9 passed, 0 failed. Landmarks 110 09EAC0FE, unchanged. Fauna_Fingerprint F4E66093, unchanged. Editor closed.

## A-78 (animal), rows 1-2

### Row 1: cave folk on v3, done (verified)
- Fix 7a9d026: `-incave` / `-place batfolk` now re-place the player each time v3 locations change (180 s window), so the player lands in the bat folk chamber instead of the open.
- Natural v3 spawns from the log: CavePygmy 3 in all (1 homed at a wall niche); BatFolk 1 chamber band, 6 in all (chamber 0, cave 5, r 15). With `-spawn CavePygmy:4 -spawn BatFolk:4` the framing probe finds CavePygmy n 7 and BatFolk n 9 in the chamber.
- Shots (Assets/Temp/shots): folk-p is a bareheaded grey cave pygmy with a spear, from about 6 m. folk-a is a bareheaded bat folk at about 7 m, saying "Too bright... too bright...". folk-bf is blocked by a hide door panel, so use folk-a for bat folk.
- Risk: the framing camera comes in from above and behind. These are not face portraits.

### Row 2: next colossus (#40), CinderSnail in EmberForest, done (verified)
- Commit ff9e4d6 (StoneColossi.cs, ColossusWalker.cs).
- Biome list, % of land, then colossus:
  - Temperate 11.3: head
  - Ashlands 12.2: lizard
  - Mountains 10.5: titan
  - SparklingForest 8.0: stag
  - FungalForest 7.8: tortoise
  - EmberForest 6.9: none, so the CinderSnail goes here (new)
  - MesaCanyonlands 6.9: mesa tortoise
  - Tundra 6.4: titan
  - ElectricWastelands 6.4: golem
  - Glacier 4.0: titan
  - Dunes 3.9: DunesColossus
  - Grassland 3.6: head
  - Sonoran 3.6: saguaro
  - FoggyMarsh 2.2: none
  - SaltFlat 1.9: crystal
  - GlassDesert 1.8: heron
  - PoisonSwamp 1.5: plant
  - Ocean 1.1: none
  - Volcano 0.0: none
- EmberForest was the largest biome without a colossus (Ashlands left out).
- The giant: a snail with a coiled cooled-lava shell (2.6 turns), ember seams and crust lumps, a flared lip with fire mushrooms, ember eyes on tall stalks, and an ember-glinting skirt. Details: a smoking vent with a crow, a charred tree with roots and ember leaves, a baby snail, and a rope ladder with one slat missing. The eyes and seams glow within 450 m.
- Placed at (2199, 14.2, -2575), facing 32 degrees. It walks a 255 m loop at 0.4 m/s using A-77 steering: 120 s walking, then 60 s resting. Flora is kept 70 m out. Default box collision, far shadow cast (36, 16).
- Render: cs-far 1.64 ms (frame 16.78, cpu main 7.05, gpu 0.23); cs-near 1.57 ms (frame 16.67, cpu main 6.09).
- Fauna_Fingerprint F4E66093 (unchanged).
- Shots (Assets/Temp/shots): cs-far (250 m, the shell over the red pines, with smoke), cs-near (48 m), and a strip of cs-near, cs-s1 and cs-s2.
- Risk: the strip is weak. `-walkershot` only frames the first shot, so after that the camera falls back to the player. cs-s1 has the whole snail; cs-s2 has only its edge.
- Fix in the reshoot: the shell first floated about 3.5 m above the foot. I lowered the coil axis from 17 to 13.5 and added a mantle under the shell.
- Suite: WorldTests 9 passed, 0 failed (199 s); landmarks 110 09EAC0FE (unchanged).

## A-79 row 1: fixed-camera shot flag (done)
- Commit 4235ec4 (DebugControls.cs).
- New flag `-shotcam x,y,z,tx,ty,tz` (shot lists / PlayArgs). It pins the camera at x,y,z, looking at tx,ty,tz, for every frame of every later shot until `-shotcam off`. It runs after the other shot helpers, so it wins over `-walkershot`. `-walkershot` can still sit on the same lines to keep the player (and the streamed ground) near the subject. The usage line is in the DebugControls help comment.
- Normal play is unchanged: the flag is set only in ApplyOptions, and ShotCam does nothing while it is off.
- Snail strip retake: `-shotcam 2199,48,-2715,2199,22,-2575`, 30 s apart, walking. Shots a79-cs-s1, a79-cs-s2 and a79-cs-s3 (Assets/Temp/shots) have the whole snail in every frame, creeping slowly across the frame at 0.4 m/s.

## A-79 row 2 - FoggyMarsh colossus: the mire walker (#40) - commit d55718a

**Design:** the thirteenth colossus, a hunched marsh giant built in the existing colossus style. It has:
- 22 mangrove prop roots, each with a mud mound and rootlets
- a body and hump covered in a moss sheet, 34 strands of beard moss, and shelf fungi
- a long mossy skull on a neck, with an open jaw and two glowing fog eyes
- dead-branch antlers draped in beard moss, with a rusty lantern hanging from the right one (glows)
- a heron standing on the hump, and a reed shrine with a candle (glows)
- a snagged fishing net with a fish in it
- mist puffs and firefly puffs round the roots

**Placement and movement:**
- It is placed in FoggyMarsh only, at about (-1846, 134, 135), facing 350 degrees (region 21 cells, relief 7.2).
- It walks a 230 m loop round (-1804, 145) at 0.6 m/s using A-77 steering (radius 36, 5 shrinks). It has no rig: it glides on its root skirt with a small bob and a camera shake.
- It has box collision and a far shadow (44 x 18).
- Flora is kept 61 m clear of it.

**Shared file (additive only):** the existing 12 keep-out slots were full, so I added a fourth static, `KeepOutFinal` (slots 12-15), and a matching loop in `Flora/FloraPlacement.cs` Place. It is wired into StoneColossi and ColossusWalker. **The world feature manager needs to be told**, since they own that file.

**Perf:**

| Shot | Render | Frame | CPU main | GPU |
|---|---|---|---|---|
| far | 2.34 ms | 16.87 ms | 7.50 ms | 0.26 ms |
| near | 2.13 ms | 16.84 ms | 7.05 ms | 0.26 ms |

Brain is about 1.0 to 1.3 ms. Fauna_Fingerprint is F4E66093 (unchanged). No compile errors.

**Shots (in Assets/Temp/shots):**
- `a79-mw-s1..s3`: the 3-frame strip, taken with `-shotcam -1804,192,30,-1804,150,145`. The whole giant is in every frame, moving left across its clearing, with mist at the roots.
- `a79-mw-face`: a close, low view of the roots and neck.
- `a79-mw-far` and `a79-mw-near`: tree canopy blocks the view, because the walker-shot camera sits inside the forest. A retake from a `-shotcam` vantage is queued. Until then, use s1 as the far shot and face as the near one.

**Risks:**
- The fog is weak in daylight, so it reads as mist round the roots, not as the giant looming out of fog.
- The marsh spot is at 134 m height. The giant is clear of the water mesh.

**A-79 row 2 retake, and the suite:**
- **New shots.** `a79-mw-far`, `a79-mw-near` and `a79-mw-night` replace the canopy-blocked far and near shots.
  - Both new views were pinned with `-shotcam`, from the clearing.
  - Far: the giant looms out of grey rain and fog across the marsh.
  - Near: shows the whole body, the roots with mist, the heron, the antlers and the shrine.
  - Night: the eyes and lantern glow.
  - Rain happened to be on, which helped the fog read.
  - An "E sit" prompt shows in each frame. It is HUD noise from a seat near the player, not part of the colossus.
- **Perf (retake):**
  - far: render 1.85 ms (frame 16.81, cpu main 6.72, gpu 0.20)
  - near: render 2.06 ms (frame 17.05, cpu main 7.09, gpu 0.21)
  - brain 0.9 to 1.3 ms
- **WorldTests:** 9 passed, 0 failed in 183 s.
  - Landmarks are still 110 09EAC0FE.
  - Fauna fingerprint is still F4E66093.

## A-80 rows 1-2 (2026-11-06): one spawn-table system, done (verified by numbers)

Commits 3560d7e (r1) and 246f7a1 (r2) on animal-behaviours.

- **Row 1:** `SpawnTable` asset (design note at the top of Scripts/Fauna/SpawnTable.cs). DungeonSpawner picks from Resources/SpawnTables/Dungeon.asset (36 rows).
  - Dungeon 89 type 1: 73 sockets, hash 0x558a105b (again 0x558a105b, same). The second editor run comes with the row 3 run.
- **Row 2:** CritterTable, DomesticTable and their assets are removed. Critters, domestics, wild folk, the traveller, the messenger, the honey thief, crossing fishers and cave folk now pick from Resources/SpawnTables/Overworld.asset (47 rows, Kind = OverworldSlot).
  - Fauna_Fingerprint: F4E66093, unchanged, 117 species.
  - Spawn ms: the build species stage took 3 ms (it was 3-9 ms before). cpu main was 6.72 ms by day and 6.84 ms at night (player at spawn). A pick scans 47 rows and makes no extra rng draws.
- **Risk:** commit r1 alone does not compile, because the old table deletions went into it. r1 and r2 together do compile. Rewriting the pushed commits was blocked, so they stay as they are.

## A-80 row 3 (2026-11-06): caves fill from their own table, done (verified)

Commit 59f4082 on animal-behaviours.

- **Cave.asset** (Resources/SpawnTables, 24 rows): rows per chamber type (Chamber, PoolChamber, DeepChamber), den visitors in passages, and moths by glow. W-115's socket biome and chamber tags are not on main, so the rows key on the cave graph's own node kinds.
- **Time-of-day example:** bats roost in chambers by day only (Times = Dawn|Day).
- Chambers fill first, so a full cave cap (100/100) no longer starves them.
- **Counts per table on v3:** Overworld 47 rows, Dungeon 36, Cave 24.
  - Cave picks by day: Chamber Bat 16, CaveSalamander 15, empty 23. DeepChamber Bat 10, CaveSalamander 11. PoolChamber CaveSalamander 8.
  - Cave picks by night: no bats. Chamber CaveSalamander 17, empty 37. DeepChamber CaveSalamander 19, Tarantula 2.
  - Cave 5, filled live at noon: Chamber node 44 CaveSalamander 2/2, PoolChamber node 22 CaveSalamander 3/3, Chamber node 33 Bat 3/4, and 6 more chambers.
- **Shots:** `a80h-ch44` (chamber 44 floor with its salamanders) and `a80h-pool22` (pool chamber), both in Assets/Temp/shots. The creatures are small in frame; the fill log names what each chamber got. "E sit" is HUD noise.
- Fauna_Fingerprint is still F4E66093.
- **Note:** GiantSpider (DeepChamber, Temperate only) never got picked on v3.
- **A-80 suite:** WorldTests 9 passed, 0 failed in 172 s. Landmarks 110 09EAC0FE, fingerprint F4E66093.

## A-81 row 1 (2026-11-06): colossus definitions and the StoneColossi split, done (verified)

Commit c98e94e on animal-behaviours.

- **ColossusDefinition assets** (Resources/Colossi, 16 assets: 13 primaries in placement order plus the 3 `-colossus` swaps) replace the parallel Kinds/Biomes/Salts/KeepRadius/Casts arrays. Every salt is copied unchanged.
- **Split:** one `StoneColossi.<Kind>.cs` partial per builder (16). The core StoneColossi.cs is 27.9 KB (the limit was 40 KB).
- **Design note** (StoneColossi.cs remarks): to add a 14th colossus, append a Kind enum word (never reorder: assets store the number), add a `StoneColossi.<Kind>.cs` builder that registers itself, and add a definition asset with its own salt (plus a Gait line in ColossusWalker if it walks). The enum word is the one code edit left.
- **Positions:** all 13 are the same before and after: KneelingTitan (-1990,177.9,1321), HalfBuriedHead (1716,23.0,2493), GiantTortoise (-1502,2.8,-2769), RockGolem (707,48.8,-329), CarnivorousPlant (-53,34.3,516), SkeletalLizard (-515,26.3,-2477), WalkingSaguaro (2925,14.6,-1710), SaltCrystal (3702,7.3,-917), MesaTortoise (2026,141.8,1344), GlassHeron (2629,10.6,2034), LanternStag (-2637,110.1,-320), CinderSnail (2199,14.2,-2575), MireWalker (-1846,133.9,135). The 8 walkers walk the same loops.
- Fauna_Fingerprint is still F4E66093 (this branch is from before PR #170).
- **Shared file:** this row is not additive in Flora/FloraPlacement.cs, as the card asked. The four fixed keep-out statics are replaced by one `ColossusKeepOut` SharedStatic list (room for 31), filled through `FloraPlacement.SetKeepOut(slot, circle)`. ColossusWalker now finds its slot from `StoneColossi.Placed`, and ColossusRig picks the rig from the definition's rig type.
- **Shots:** `a81a-saguaro` and `a81a-crystal` in Assets/Temp/shots; both colossi are built and in place.

## A-81 row 2 (2026-11-06): race tags, done (verified)

Commit faf907f on animal-behaviours (after merging origin/main with PR #170, merge b4e6443).

- **FolkRace assets** (Resources/Races, 10 races) replace the ComfortRace enum. Each asset holds the race's id (the old enum number, unchanged), species, comfort numbers and traits: Folk, ColdAdapted, HeatAdapted, Aquatic, Fishing, CaveDweller, Spirit.
- **Burst access:** jobs ask `FolkRaces.Has(id, trait)`, which reads a SharedStatic trait table.
- **No ComfortRace is left** in Assets or docs. Folk checks use the Folk trait. The Fishing trait (not the race) picks who takes a wharf for the Fish task and who takes the cave-pool chore. Weather talk keys its lines by the race asset name.
- Fauna_Fingerprint is **90AC5F60**, the same as main after PR #170: the baked comfort bytes are identical.
- **Folk check:** FishingVillage, HumanVillage and SeaFolkVillage load; `[comfort] race Human` is printed by name.
- **Shots:** `a81b-fishing`, `a81b-human` and `a81b-seafolk` in Assets/Temp/shots.
- **Risk:** Fishing is on Human, Elf, Dwarf, Goblin, FishFolk and Pygmy, which are all the races that fish today. Ogre, BatFolk, Hermit and Ghost don't have it. A future table that gives one of those races the Fish task will not send it to a wharf until the trait is added to that race's asset.

## A-81 row 3: climate readers, skipped

`Climate.At` is not on main, even after the PR #170 merge, so there is nothing to route the readers through yet. This can be picked up once the world lane lands it.

## A-81 row 4 (2026-11-06): FaunaDirector and CreatureRenderSystem split, done (verified)

Commit 975a496 on animal-behaviours. Only code moved. A line-multiset check shows no line was removed or changed; the only line added beyond the file wrappers is `partial` on PoseJob.

- **FaunaDirector.cs** went from 117.6 KB to a 34.7 KB core, plus these partials:

  | Partial | Size |
  |---|---|
  | Build | 22.5 KB |
  | Avatar | 9.6 KB |
  | Folk | 22.8 KB |
  | Visitors | 10.2 KB |
  | Critters | 13.0 KB |
  | Caves | 7.7 KB |

- **CreatureRenderSystem.cs** went from 187.6 KB to a 39.4 KB core, plus these partials:

  | Partial | Size |
  |---|---|
  | Extras | 22.9 KB |
  | Passes | 23.7 KB |
  | PoseGear (PoseJob) | 44.8 KB |
  | PoseGuard (PoseJob) | 35.1 KB |
  | PoseHomes (PoseJob) | 24.1 KB |

  PoseJob is now a `partial struct`.
- **Size:** every file is under 60 KB.
- **Run:** compiles clean, Fauna_Fingerprint 90AC5F60. Shot `a81c-fishing` in Assets/Temp/shots matches a81b-fishing.
- **A-81 suite:** WorldTests 9 passed, 0 failed in 190 s. Landmarks 110 09EAC0FE, fingerprint 90AC5F60.

## A-81 row 5 (2026-11-06): FaunaDirector.Update spikes, done (verified); GiantSpider in v3 caves, done

Commit c94a99e on animal-behaviours. origin/main is merged in.

- **Cause:** the spike was Comfort.Refresh. Once a second it took 225 managed weather and ground samples for the 15x15 comfort grid in one frame, about 40 us each. Measured before (fishing village, v3): 8.3-9.3 ms every second, up to 23.5 ms. The director's other passes were about 0 ms.
- **Fix:** the refresh is time-sliced.
  - It fills one grid row a frame into a back grid, then publishes the whole grid when the row sweep ends. Readers never see a half-filled grid.
  - A new sweep still starts once a second.
  - There is a guard so it does only one row per frame, even with two callers.
  - A joined client (WeatherTalkSystem) now calls it every frame for the same reason.
  - The new marker FaunaDirector.Comfort sits inside FaunaDirector.Update.
- **After** (ProfilerRecorder on the markers, 2391 frames at 60 fps, fishing village):
  - FaunaDirector.Update: mean 0.18-0.27 ms. Only one frame went over 2 ms: 2.27 ms, the one-off resident peopling on arrival.
  - FaunaDirector.Comfort: at most 1.07 ms.
  - Not part of this row: the first frame after a teleport still costs more (warm-up for crossing and resident spawns).
- **GiantSpider:**
  - **Cause:** its deep-chamber row in SpawnTables/Cave keyed only on Temperate. On v3, no cave mouth opens in Temperate. Mouths by biome (from the new spawn-tables log line): Mountains 5, SparklingForest 4, Tundra 4, Ashlands 3, FungalForest 3, ElectricWastelands 2, and 1 each in FoggyMarsh, GlassDesert, MesaCanyonlands and Sonoran.
  - **Fix:** the row now also takes Mountains, FungalForest, SparklingForest and FoggyMarsh. This is a table-only change.
  - **Result:** on v3, GiantSpider is picked in 1 deep chamber by day and 4 at night, out of 21.
- **Suite:** WorldTests 9 passed, 0 failed in 167 s. Landmarks 110 09EAC0FE, fingerprint 90AC5F60.
- **Not mine, for the world lane:** one earlier cave-shot run logged 4 "BlobAssetReference is not valid" errors in the Burst BuildChunkMeshJob, via TerrainSource.PondDig/SampleHeight. They came after the shot list ended, which looks like play-mode-exit teardown. The run before and the suite logged none.

## A-82 rows 1-4 (2026-11-06): #60 table, GC, far-band shadows, colossus rig hitch: done (verified)

Commits on animal-behaviours: row 1 de458d0, row 2 eb5f0d7, row 3 929e8e9, row 4 65e2ce7. origin/main is merged in. Fauna_Fingerprint 90AC5F60 in every run.

- **Row 1 (#60): Decide is a table.**
  - Shape: `enum DecideStep` lists the forced and urgent steps in priority order (Shield, Flit, Breach, Patrol, Hermit, Camper, FisherSpot, Rooted, Disguised, Senses, Nest, HuntGame, Raid, PlayRing, Hearth, Roll, Greet, Roost, Burrow, Chores). `TakeStep` has one case per step, holding its original condition and start; returning true ends the choice. A `Choice` struct carries sleepy, the senses, the weights and the roll between steps. The weighted tier is unchanged (Weighted, Weights, StartWeighted).
  - Adding a forced or urgent activity means one DecideStep entry where it ranks plus one TakeStep case. A weighted activity is still one Weighted name, one weight and one StartWeighted case.
  - The random draws are unchanged and in the same order; the roll is still drawn between Hearth and Greet. The negated guards (HuntGame, Nest) keep the same short-circuit order.
  - FolkChat mixes at HumanVillage, FishingVillage and SeaFolkVillage, at 8 h and 13 h: the same activity families as the baseline run. The counts drift within a run, and the after run shows Sit (bench and fire seats, set in Places/Folk) and Shelter (comfort). Both are set outside Decide, and the folk sim depends on frame timing. SeaFolkVillage has 0 folk within 80 m in both runs.
  - I commented on #60 with the commit and did not close it.
- **Row 2: GC per frame** (HumanVillage 13 h, 120 frames, editor play mode):
  - Total before: 11432 B in 150.5 allocations. After: 10093 B in 142.1.
  - Mine, before to after: CreatureBurrowSystem 293 to 17 B. FolkTalkSystem 286 to 40 B. CreatureWakeSystem 165 to 17 B. InteractionPrompt.OnGUI 204 to 136 B. DebugControls.Update 121 to 88 B. StoneColossi about 150 B (0.2 allocations a frame) in both runs.
  - DebugControls.LateUpdate showed 508 B in the after run. That is the -comfort debug logger (shot-list flag only), not game code.
  - OnGUI scripts. Mine: FolkTalkLabels, InteractionPrompt, DebugControls (all now useGUILayout=false). Others': Dungeon/TempleSockets, Interact/Doors, Interact/Interiors, Items/Items, Net/NetMenu, Settings/FrameRateCap, UI/MainMenu.
  - **For the player lane:** Items.Update about 3600 B a frame, Doors 88 B, Interiors 48 B. Shared IMGUI GUIUtility.BeginGUI went from 3312 to 2576 B, and the rest of it comes from the other OnGUI scripts that still use GUILayout. UI/Prompts.cs allocates a RectOffset on every `style.padding` call.
  - **For the world lane:** FaunaDirector.Comfort about 1140 B (WeatherField closures), WeatherSystem 352 B, LandmarkEffectSystem 219 B, FloraSystem.LateUpdate 206 B, CaveSystem 176 B, GroundDetail and AmbientParticle 32 B each.
- **Row 3:** the far creature band (>100 m) draws with ShadowCastingMode.Off, like colonies. I didn't take a separate shot because nothing else changes.
- **Row 4: colossus rig hitch.**
  - Saguaro, Tortoise and MesaTortoise weights (and the saguaro trunk fit) now run in Burst jobs. They are scheduled when the rig is built and finished on a later frame. Pending jobs are completed and their arrays disposed on ColossusWalker OnDestroy and when a walker's root is dropped, so nothing leaks. Heron and stag rigs are unchanged.
  - The fit matches the earlier log exactly: "saguaro skinned (178950 verts, trunk fit 47 m tall leaning 6.5 m, 2 arms)". The tortoise has 7356 verts and the mesa tortoise 16248 verts.
  - Before: a 460 ms frame at the saguaro build. After: main-thread finish (bones and skin) takes 12.9-17.4 ms over 4 runs. The tortoise takes 1.1 ms and the mesa tortoise 4.0 ms.
  - Frame times from a ProfilerRecorder on Main Thread, saguaro walkershot, uncapped: the rig frame (896) was 24.5 ms, under 33 ms. The frames either side were 15.9 and 17.6 ms. The 221 ms frame at 893 is the walkershot teleport itself.
  - Shots: a82-saguaro, a82-tortoise and a82-mesa in the scratch shot folder. The first try framed nothing, because walkershot matches by prefix ("Walking", "Giant").
- **Suite:** WorldTests 9 passed, 0 failed in 193 s. Landmarks 110 09EAC0FE, fingerprint 90AC5F60.
