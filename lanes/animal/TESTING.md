# Animal lane: testing recipes

Read `lanes/shared/checks.md` first.

## Watching a creature
- `-watch <Species>[:Mode]` frames a creature (`-watch Human` picks your own body: play as `-body Dog`).
  `-spawn <Species>:<n>` spawns near you; spawned animals are wild (a cat squirms free of a carry in 2-5 s).
- Side views: `-goto x,z` plus `-camlook`; open meadow about (-255, 27), ground 35.4. `-camlook` on a line that
  also has `-spawn` doesn't take: stand the player where you want to look from.
- A lone palm: `-place Dunes -place tree:Palm`.
- Hunt side view: `-goto -255,27` (open meadow), then `-spawn Wolf:2 -spawn Deer:4 -spawn Rabbit:4 -huntlog -shotonhunt
  -huntside 6 -wait 90`. The huntside log line gives the plants counted between camera and wolf (0 = clear).
- Since A-23 `-huntside d` sits d m off the hunter's own heading (Yaw) every frame; add `-huntpair` to aim at the
  middle of hunter and prey (steps back if they are far apart). Dog chase: `-hunter Dog -huntside 5 -huntpair`.
  Back-to-back -shotonhunt lines skip the last line's hunter until its sprint ends (A-23 extend shot caught it walking).
  The shot can still fire at sprint start (A-25: 6 m/s, leap only part blended): read `hunter speed` in the huntside
  line and retake under ~10 m/s. Leg angles without a build: Temp/a25_angles.cs (eval builds the Wolf blob).
- Spin and drift probes: `-spincheck` (spin log lines; compare counts over 3 runs), `-footdrift`.
- Since A-26 `-chattest` (launch arg) forces folk talks (15-30 s cooldown); `-chatshot` on a shot line frames the
  nearest talking pair 4 m side-on once the line is 2 s in. Talks burst in the first ~20 s after `-place`, so put
  `-chatshot` on the `-place HumanVillage` line itself. Each talk logs `FolkChat: A & B at (x, z) in Town for N s`.

## Known spots
- Our seed (1337) has no mining camp. Seed 3 (local branch tmp/animal-slot8-seed3 866dc99, World.Seed 3) has a
  human camp at (209, -3878), mine at (316, -3955): `-goto 209,-3878 -time 10`, then `-watch Human:Mine`. Miners
  reach the face about 2 minutes in.

## Multiplayer checks
- Host: `-host -body Human -shotlist <list>` with `-spawn Cat:2 -wait 25`, then `-watch Cat -wait 0.5` lines.
  Client: `-join 127.0.0.1 -body Human -interact Cat:carry`. Without `-host` both run solo. On the host,
  `-watch Human` doesn't find a remote player's body.
- Remote roll (#48, A-16): host `-host -body Human`, list: `-goto -255,27 -wait 45`, then
  `-actiontest roll:Human,Human,Human -wait 14`. Client (start 12 s later) `-join 127.0.0.1 -body Human`, list:
  `-goto -245,27 -remoteroll -wait 70`: logs the first remote Roll, frames 5 m side-on, shoots remoteroll.png at 0.4.

## Gotchas
- Worm arcs start deep, so obstacles under about 20 m ahead are crossed underground.
- Paw prints use TraceSystem Underfoot; check them on benches and bridges.
- `-spincheck` is read per shot-list line; `-autowalk` only from the launch line. Without both, a spin or climb run logs nothing.
- A wall climber's mid-climb shot: about 2 s after `-place wall` (the Lizard climbs 3.4 m in about 3 s).
  `-place wall:Lizard -wait 2.5` frames it from 9 m out along the rim's outward normal (28 deg view): Lizard about
  0.5 m up the barn face. A camera beside or inside the building dithers the face away (PlayerReveal).
- `-shotonhunt` ends its line at the first sprint, so later logs on that line (`-personalitylog`) never print: put `-personalitylog Dog -watch Dog:Hunt` on its own line. Growl lines start with `CreatureAudio:`, not `DebugControls:`. `-huntside` takes the first land hunter to sprint (it may be a wolf).
- `-warp X N` counts from the player's position (nearest first), so a warp line after a -goto picks different targets. To match earlier warp shots, put the warp lines right after the load line.
- A-42: `-pose X:lie` (lying, legs folded), `-pose X:scare` (stands 3 s then flees +z), `-oldlie` (old lying legs, per line). Pose waits 5 s after each line and is cleared per line, so repeat it.
