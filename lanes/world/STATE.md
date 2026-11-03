# World lane state (after W-35b, rows 1, 2 and 5 DONE; rows 3 and 4 DONE with exceptions)

- world-features head: f770492 (W-35b). Before that 287497c (W-33).
- W-35b: `RiverBaking.ReshapeLoweredLakes` runs after LowerBreachedLakes. It reruns the BedDownhill passes on the
  cells whose base level dropped or that stopped being lake, so their water never stands above the ground or rises
  along the flow. Every other cell keeps its bed and water exactly. Documented in docs/agents/world.md.
- Hashes now (both islands changed, as Zach allowed for the riverbed reshaping; landmarks did not move):
  - v2: landmarks 126 / 053C63D5 (player and editor; the WorldProbe list matches line for line).
    Caves 5D90D9CFC5326976 -> ACA9CC9155621A44. Rivers FD0045F0A0810452 -> 6A1BC5BDCDE399BE.
    Ground 4E0B705A4BDE0ECD -> 79A65F834D760FC8. Falls 75 -> 70.
  - v1: landmarks 102 / 51EA6463 (player and editor; the list matches). Caves 47C703C0E17F77CB, unchanged.
    Rivers 164DF6A9926DE097 -> B867BA86DC27D8D0. Ground 3EE84EED3A33770B -> 2AC3143B4715222E.
  - The rivers and ground hashes come from the W-35b measure script (an FNV hash over the river blob arrays, and
    over a 64 m grid of heights). They are not a game log line.
- Tools (untracked, world tree Temp): w35b_measure.cs counts steps, open ends, the grove and the site-1 line.
  w35b_edges.cs is the open-end edge rule, which caught -879,1001 before the fix. w35b_grove.cs lists the grove's
  dry points.
- Still open:
  - Steps over 0.3 m: 44 unexplained step cells on v2, 13 on v1, listed in REPORTS-world.md.
  - Edge-rule open ends that are not at falls: 106 on v2, 3 on v1.
  - Grove: 31 dry points at the channel edge. They are bank, 0.2-0.55 m above the water.
- A player defaults to v2. For v1, pass `-island v1`. A `-shotlist` needs `-shotdir`, or it never reaches `-quit`.
- Shots: PunchyMilestones/world/w35b (8 river shots + 2 X1 shots), plus w35b/v1 (1 X2 check shot).
- Editor ports are swapped today: target the world editor by PID and project path, not by port.
