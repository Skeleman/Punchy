# World lane state

Updated 2026-11-02, after W-30 (archipelago). Branch world-features @ 8565688 (pushed). W-35 next.

## W-30 done
- Island/Archipelago.cs: v2-only overlay baked after places (like GrandFalls). 11 islets, 4 sandbars, from the dune
  coast at (2972,2159) heading 36 deg, out 1.7 km; land x 3122..4252, z 2323..3290 (coast-square limit 4486).
  Biggest islet (3224,2425) 205 m wide. `-archipelago off|on` swaps it in a shot list.
- Release players report no draw-call/tris recorders (Valid false); stats come from UnityStats in editor play mode.

## Open
- Archipelago: islet flora is dense palm groves (card asked for "a few palms or scrub"); aerial reads hazy at 250 m.
- v2 bridges per island not counted. Cobble cost not benched. From W-28b: taller dry-dock cave berg. From W-29:
  slanted sheet beside the main falls, snowy levees, plain gorge walls.

## Hashes (re-read 2026-11-02 with W-30)
- v2 126 / 053C63D5, caves 5D90D9CFC5326976.
- v1 102 / 51EA6463, caves 47C703C0E17F77CB.

## Notes
- Editor eval: `unity command --port 7801 script eval_file Temp/x.cs`. The editor process has PUNCHY_ISLAND=v1;
  for v2 in play mode, `Environment.SetEnvironmentVariable("PUNCHY_ISLAND","v2")` in an eval first (Temp/w30_v2play.cs).
- Shot lists: lines without `-shot` (the -wait and -quit lines) also save shot-N.png; delete those.
- Shots: `Builds/Dev/Punchy.exe [-island v1] -shotlist <file> -shotdir <dir> -logFile <file>`.
