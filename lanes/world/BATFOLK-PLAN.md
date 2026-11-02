# Bat folk caves: plan (#35, no code yet)

**Hash answer:** no. Settlements go inside caves that already exist, in a pass after CaveBaking, so landmark
placement never sees them: v1 stays 101 / E128CB1D and v2 127 / 831FF2EE.

## Where
- Only caves that are already baked. No new cave landmarks and no new cave spots.
- A cave qualifies if it has a Chamber node with radius >= 9 m that is at least 3 nodes in from the mouth, so it
  is out of daylight, and at least one water pool node in that cave or the next chamber along.
- Up to 1 bat folk cave per 6 qualifying caves. Choose them by a scrambled hash of the cave's mouth cell, not
  in list order, so a change to one cave's style doesn't reshuffle which caves are picked.
- One settlement per cave, in its biggest qualifying chamber: 3-6 roost huts on the walls and ceiling (Roost-style
  anchors, CaveDistance near 0), a floor ring of 4-8 props, and the chamber's own pool turned to white pool.

## Not moving landmarks
- It is a new stage, `BatFolkBaking.Build(source with Caves)`, after CaveBaking and before AnchorBaking, with its
  own blob (or extra arrays in CaveSetBlob added at the end).
- It reads caves only and writes nothing that landmarks, settlements, rivers or ponds read.
- It has its own RNG stream (seed ^ "batfolk" scrambled), so drawing more or fewer bat folk never moves other draws.
- Its props avoid speleothems and rubble by a distance check against those lists only (read-only).
- Checks: landmark count and hash for v1 and v2; cave hash (WorldProbe.CaveHash) unchanged with the stage off.

## No-light look
- Bat folk see by sound, so nothing they make gives off light: no lanterns, no fires, no glow crystals they place.
- What shows the chamber is the white pool (self-glow 0.28, see `CaveSystem.WhitePoolWater`) plus the existing glow
  plants. The white pool needs a soft light so it lights the walls a little; at present the glow is emissive only.
- Huts are pale hanging pods (silk and hide), darker underneath, so they stand out against the dark rock even in
  the pool's light alone.
- Needs one cave ambient floor value per settlement chamber, so shots are not pure black away from the pool.

## Batch steps
1. Stage plus data: qualifying caves, choice, chamber and pool picks. Log `[batfolk] caves N, picked K`. Hash checks.
2. White pool per pool: a per-pool flag in CaveSetBlob in place of the `-whitepools` test flag (draw two meshes).
3. Huts and props: flora-style assets (BatHut, BatRack) placed from the stage's anchors. Shots in 2 chambers.
4. Light: a pool point-light or a cave ambient lift in the chamber. Dark-chamber shot pair (with and without).
5. Residents: hand bat folk creatures to the animal lane (roost anchors are already the shared contract).
