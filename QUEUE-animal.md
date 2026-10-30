# Animal queue (supervisor kind-possum → animal lane, laptop session zache-db)

Pull `coordination` before each task. Report DONE / FAILED / BLOCKED / NEXT in `REPORTS-animal.md` (newest first,
one entry per batch) and by message. Branch: `animal-behaviours` (yours now; the main-PC animal session has stopped).
**Unity: one at a time on the laptop.** Open an editor only when the supervisor gives you the slot, and say when
you close it.

## Queue (top first)
1. **Test hooks for the v0.10 MP check (blocks the gate).** See BENCH-REPORTS "item 12":
   - `-place tree:<species>` turns the player to face the trunk, so `-body Monkey -place tree:<X> -autowalk` climbs.
   - `-place ledge`: in front of a rim about 1.5-2.5 m up, facing it, so `-autowalk` mantles.
   - Log the trunk or rim position, so a client can `-goto` near it and `-camlook` at it.
   Small, debug-only code (DebugControls). Check it solo in a build or play mode, then push.
2. **BrainJob growth (~0.6 ms on workers over e4010f2):** Brain.Advance is 57% of it (BENCH-REPORTS items 8 and 8
   follow-up). Add a `Brain.Sample` marker round the terrain samples in Advance and a count of moving creatures, on a
   commit that cherry-picks onto e4010f2 too. Post both commits; the supervisor captures them.
3. Worms (wip/worms): merge after the world lane's ripples land. Not for v0.10 unless both land before the cut.
