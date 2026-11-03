# World lane state (after W-35, NOT DONE)

- world-features head: 8565688 (W-30 archipelago). W-35 left no code change: the try was reverted.
- Hashes unchanged: v2 126 / 053C63D5, caves 5D90D9CFC5326976; v1 102 / 51EA6463, caves 47C703C0E17F77CB.
- W-35 cause found: RiverBaking.LowerBreachedLakes lowers a breached lake (around 68.3 -> 55.9 near -632,1325) but leaves
  the old levels behind. Rivers that backed up into the old surface keep water near 68.3, and the old lake's shallow cells
  get bed = cell elevation, so the water rises and falls along the river and steps 1-5 m.
- Two water-only refill passes were tried; the hashes held both times, but neither is good enough to ship (numbers in REPORTS-world).
  The real fix probably needs the old lake cells' beds re-shaped (BedDownhill loop 1 rerun on them). That is bed carving and may move hashes, so it needs a decision.
- Before shots: PunchyMilestones/world/w35 (8 shots, v2 day, clear). The aerial cameras sit in forest, so redo them higher next time.
- NEXT: decide whether bed reshaping of breached-lake cells is allowed; then redo rows 1-4 and take after shots.
