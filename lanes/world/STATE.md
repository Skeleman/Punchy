# World lane STATE (after W-22, 2026-11-02)

Pushed head: world-features 5016957 (W-22: d436650 biome-blend fix + 5016957 v2 default / falls). Builds/Dev holds the W-22 build (= 5016957).
Player logs: no flag = v2 125 / AFA9CC63 (caves D531293D622E785A); `-island v1` = v1 101 / E128CB1D.

## Island
- #23 v2 island: `Terrain/Island/IslandV2.cs`. v2 is the DEFAULT since W-22; `-island v1` / PUNCHY_ISLAND=v1 for v1.
  No netcode sync: host and client each read their own launch (a v0.11 peer defaults to v1 and would mismatch).
- W-21 biome-blend fix applied (BiomeData.SampleMapBlurred: sum all ids, keep top 4 minus 5th). 127 -> 125 lost:
  OakGrove 1757.3,2593.3 and Waterfall -1993.9,3009.8. Caves 37 -> 37, settlements 6 -> 6, bat folk unchanged
  (cave 2 chamber 3001,3,-2135, huts 3).
- #18/#42 smooth rivers + falls (v2 only). 71 waterfalls in v2.

## Falls (W-22)
- RiverQuery.FallWater: upper surface runs to the lip (the pool's 50 m channel reached under it; 16 m cells).
  Fall 0 (lip -2450.5,21.4,-960.3) profile: water 21.4 over ground 16-19 to the lip, then 16.9. Hashes unchanged.
- LandmarkWaterfall.shader: surface fall speed 2 x fallLength/3/lifetime (fall 0: 1.15 -> 2.30 m/s); cave falls 1.62.
- LandmarkEffectSystem.FallSounds: WaterfallRush loops at the 4 nearest drawn falls; volume lerp(0.45,1,t),
  reach lerp(40,60,t), t = saturate((drop-2)/12); x (1 - smoothstep(0.6 reach, reach, d)); GameAudio log rolloff.
  Log `[falls] sound sources N (of M falls drawn), max distance X m`.

## Open
- Hut/MudHut LOD1 roof shading (W-22 row 5) NOT fixed: LOD0/1/2 meshes checked in eval: normals point out/up,
  no flipped triangles, colours/uv same. Flora.hlsl and FloraCull have no LOD-specific normal code. Needs an
  editor shot at 30-55 m to see the actual artifact (maybe the LOD0/LOD1 crossfade band or shadows).
- -2165,255 shot shows smooth hills, no walls; the lake itself was not in frame.

## Tools
- Untracked Assets/Temp/Editor/WorldProbe.cs: LmList, CaveBench, FallList, FallGo, Shot. Lists in Assets/Temp/lmlist-*.txt.
- Shot recipes: lanes/world/TESTING.md. Shots: PunchyMilestones/world/w22.
