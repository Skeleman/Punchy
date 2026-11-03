# World lane state (after W-33, DONE with notes)

- world-features head: 287497c (W-33). Before that f064b41 (W-31).
- New query: `TerrainSource.CaveDistanceBound` / `CaveSampling.LowerBound`, a lower bound on the distance to rock
  for push-out, casts and step sizes. `CaveDistance` is unchanged (carving and placement hash on it).
  Per-cave `SlopeLimit` was fitted to a probe; retune it if `WallNoise` changes. Documented in docs/agents/world.md.
- Sky.hlsl (W-33): deeper 0/+5 deg colours, a taller warm band on the sun side, half the warm tint in land haze,
  cloud glow about 2.2x from +12 deg to blue hour with a silver lining, high clouds lit to about -9 deg,
  weather mutes the glow by at most 50% (was 80%).
- Hashes: v2 126 / 053C63D5 (player log). v1 caves 47C703C0E17F77CB (editor, which bakes v1).
  Not re-measured: the v1 landmark hash in a player and the v2 cave hash. No generation code changed.
- A player defaults to v2 now. For v1 pass `-island v1`. A `-shotlist` without `-shotdir` never reaches `-quit`.
- Shots: PunchyMilestones/world/w33 (5). Most of the frame at 0 deg is still pink-violet;
  a next pass could darken the violet zenith near sunset.
- W-35 (river water steps) is still NOT DONE: it waits on Zach.
- Editor ports are swapped today: target the world editor by PID and project path, not by port.