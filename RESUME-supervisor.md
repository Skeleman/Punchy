# Supervisor resume point (11-15, end of day)

State: v0.16.0 released and playtested. All lanes stopped, no editors open. Feedback carded (PLAYTEST.md 11-15 lines).

Next, in order:
1. Merge the ready branches after review and a clean merge-tree, each followed by a punchy-sync (rule 32):
   world/world-pack e9302eb9 (W-168 r2), fauna/swimmer-splash 96f46d7e (A-125 r1 #360; tell world it adds to
   WaterPatchSystem), render/playtest-1114 (cf8ef6e9, d1b79af6, 635c59da, c1b30b46), render/cloud-blocks a0311856 (R-20 r4).
2. Restart the lanes on their queues:
   - world: W-174 (#406, one ground-edit system, top) then W-173 rows 1 and 3, W-172 row 2 (uncommitted in Punchy-world).
   - render: R-38 remainder, then R-39.
   - fauna: A-130 (reuse the W-126 mask), A-126 rows 2-4, fauna/far-life rule 11 answer.
   - folk: A-129 in-build check and row 3.
   - player (offline account): P-39, P-40, fishing build.
   - profiler: job 96 (#400).
3. Ask Zach: are the blocky squares seen from the air terrain shapes rather than cloud shadows (render note)? R-34 row 2 pick.
