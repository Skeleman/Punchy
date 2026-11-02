# Animal lane: testing recipes

Read `lanes/shared/checks.md` first.

## Watching a creature
- `-watch <Species>[:Mode]` frames a creature (`-watch Human` picks your own body: play as `-body Dog`).
  `-spawn <Species>:<n>` spawns near you; spawned animals are wild (a cat squirms free of a carry in 2-5 s).
- Side views: `-goto x,z` plus `-camlook`; open meadow about (-255, 27), ground 35.4. `-camlook` on a line that
  also has `-spawn` doesn't take: stand the player where you want to look from.
- A lone palm: `-place Dunes -place tree:Palm`.
- Spin and drift probes: `-spincheck` (spin log lines; compare counts over 3 runs), `-footdrift`.

## Known spots
- Our seed (1337) has no mining camp. Seed 3 (local branch tmp/animal-slot8-seed3 866dc99, World.Seed 3) has a
  human camp at (209, -3878), mine at (316, -3955): `-goto 209,-3878 -time 10`, then `-watch Human:Mine`. Miners
  reach the face about 2 minutes in.

## Multiplayer checks
- Host: `-host -body Human -shotlist <list>` with `-spawn Cat:2 -wait 25`, then `-watch Cat -wait 0.5` lines.
  Client: `-join 127.0.0.1 -body Human -interact Cat:carry`. Without `-host` both run solo. On the host,
  `-watch Human` doesn't find a remote player's body.

## Gotchas
- Worm arcs start deep, so obstacles under about 20 m ahead are crossed underground.
- Paw prints use TraceSystem Underfoot; check them on benches and bridges.
- `-spincheck` is read per shot-list line; `-autowalk` only from the launch line. Without both, a spin or climb run logs nothing.
- A wall climber's mid-climb shot: about 2 s after `-place wall` (the Lizard climbs 3.4 m in about 3 s).
  `-place wall:Lizard -wait 2.5` frames it from 9 m out along the rim's outward normal (28 deg view): Lizard about
  0.5 m up the barn face. A camera beside or inside the building dithers the face away (PlayerReveal).
