# World lane: testing recipes

Read `lanes/shared/checks.md` first. These are the world-specific tools and known spots (seed 1337).

## Finding a target
- `-place prop:<Species>` (any flora species: Bench, Stall, Smithy, Inn, Hall, StoneHall, Fountain, ElfBench,
  EmberPine, Woodpile, Bridge, AncientTree...); `@x,z` measures from a point, `#n` takes the nth nearest. It logs 8
  with coordinates and frames one.
- `-place cavestream[#n]`: end of the nth nearest cave stream, counted from the player, so put a `-goto` before each.
- `-place <Landmark>:centre` plus `-camrel dx,dy,dz,tx,ty,tz` (camera relative to the player).

## Many shots in one session
`Punchy.exe -benchroute -benchstops here -benchlong here 900 -weatherseed 4 -time 12 -nohud -shotdir <dir>
-shotlist <dir>/shots.txt [-benchday Stormy] [-caveshards]`
- Each list line: `-goto x,z -time h -camlook x,y,z,tx,ty,tz -wait s -shot name`. The first line is `-wait 40`
  (world and weather settle). Stop the player by PID when the log says "shot list done".
- `-weather <Preset>` per line forces weather over the player (Rain, Storm, MarshFog, Overcast; Clear clears).
- Probes: `-waterprobe 1..4` (foam / thickness / alpha / foam parts), `-caveshards` (ownerless cave walls magenta,
  logged), `-meteors` (meteor shower and aurora every night), `-menu <Tab> -forcepad xbox` (menu with pad glyphs).
- A probe build (Debug.Log blob data, exaggerated values) settles "why doesn't it show" faster than tuning. Splitting
  a shader term into colour channels found Z-34's cause after two wrong guesses.

## Known spots
- Waterfall: landmark ~(627.5, 2526), pool centre (629, 2518): `-place Waterfall:centre -camrel 6,4,-15,-1.5,12,9`.
- OgreCamp is clear (fire ~(-359, -4061); stall (-355.6, -4079.4)). GoblinCamp sits in fog. Elf grove forum
  (260.8, 3250.9). Open meadow about (-255, 27), ground 35.4.
- Wastelands MagneticRock at (-1068, -3840).
- The HUD covers the top-left of shots if `-nohud` is missing; frame targets right of centre or below.

Island (W-22): no flag runs v2 (125 / AFA9CC63, caves D531293D622E785A); pass `-island v1` (or PUNCHY_ISLAND=v1) for
v1 (101 / E128CB1D). The editor follows the same rule, so clear PUNCHY_ISLAND for v2 and set it to v1 for v1.
