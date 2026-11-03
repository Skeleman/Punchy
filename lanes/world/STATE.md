# World lane state (after W-43, 2026-11-02)

Branch world-features, head 882af99 (W-48, W-52, W-53, W-32, W-43 local, not pushed). Worktree D:/Unity/Punchy-world.

## Hashes (editor play mode, Temp/w43_hash.cs; set PUNCHY_ISLAND=v2 in the editor first)

| | v2 | v1 |
|---|---|---|
| landmarks | 127 / 4D245E12 | 103 / 19885131 |
| caves | C49E8E3036C741FC | 47C703C0E17F77CB |
| rivers | A5B3DE5D53257332 | 2508BF538DA96D7E |
| ground | F8F938002B0CBE46 | A32E650C44A45E06 |

Unchanged by W-43 (v1 caves, rivers and ground were not re-measured after W-53).

## W-43 (882af99)

- Banner cloth: fixed phase rate; one gust per banner at a fixed scroll (wind drift no longer jumps it).
- Hall/Inn roofs start at the wall top (EaveStart). Tent panels share one sag point, so the seams are closed.
- Tent collider rings follow the hide. Tents have no support grid, so no climbing.
- Porch colliders. ChimneyTop on StoneHouse, Inn and Smithy.
- `-weather X` holds (heldWeather in WeatherSystem/WeatherField).
- Open: smoke count, particle peak and cost; 5d floating water; 5f temple walk and sanctum shot; roofline shot.

## Earlier (W-48 / W-52 / W-53 / W-32)

- W-48 cave perf: Burst camera arm probe; deep in a cave only the camera's cave chunks draw.
  - Still unexplained: frames of about 33 ms for about 70 s after load.
  - Switches: `-perflog`, `-gocave`, `-oldcave`, `-nocavewalls`.
- W-52: still water's mesh reaches over shallow beaches.
- W-53: river meanders scale with river width.
  - The v2 34.51 m dam is cut as a canyon.
  - Landmarks are placed on the jittered river map.
- W-32: SunkenTemple, one per island.
  - Positions: v2 3520,360; v1 1320,-2720.
  - Analytic collision through SunkenTempleShape.
  - Design notes: docs/agents/interiors.md.

## Open ideas (not scheduled)

- Lake backwater over low banks.
- Unexplained v2 water steps (not recounted after W-53).

## Tools

- Temp/w43_measure.cs: roof gaps, tent seams, walk sims for tents and foyers, banner replica.
- Temp/w43_hash.cs: hashes.
- Temp/w37_measure.cs: steps and open ends.
- Untracked Assets/Temp/Editor/WorldProbe.cs (leave alone): LmList, CaveBench, Shot, TowerWalk.
- The editor env defaults to PUNCHY_ISLAND=v1. PlayArgs.txt does not pick the island.
