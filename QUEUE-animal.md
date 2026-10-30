# Animal queue (supervisor kind-possum → animal lane, laptop session zache-db)

Pull `coordination` before each task. Report DONE / FAILED / BLOCKED / NEXT in `REPORTS-animal.md` (newest first,
one entry per batch) and by message. Branch: `animal-behaviours` (yours now; the main-PC animal session has stopped).
**Unity: one at a time on the laptop.** Open an editor only when the supervisor gives you the slot, and say when
you close it.

## Queue (top first)

0. **Zach's v0.10 playtest: FEEDBACK-v0.10.md.** Your rows are lane A (and shared ones). Order: P1 first (bugs), then P2, alongside the current item 1; P3 items need a design note and the supervisor's OK. Report by Z-ID.
Done since v0.10: test hooks (3025dbf), brain-sample markers (item 13/14 captured), worms (149e4b0), trunk-hug
(2272733), trunk-lean (fade08c), worm-look (d83b1ee).
1. **Worm trail of bumps (Zach):** wip/worm-trail 18f1b3b + world-shader wip/worm-trail-shader 92a9602 (zache-73 OK'd).
   Merge both together once the profiler's cost check (slot2 "worm") passes.
2. **Graphics settings (Zach):** after the supervisor OKs the world lane's settings API (QUEUE-world item 1), wire the
   creature render ranges as default × multiplier: CreatureRenderSystem FootDetail/TorchFlame/Lantern/Colony/CaveFish
   (Swarm: draw use only, not anger/chase; leave BehindCull), TraceSystem, and Burrow Range from the same setting
   as the shader gate. Rendering only: never FaunaDirector's spawn radius or relevancy.
3. Later: NPC climbers following trunk lean (FloraShape has no mesh data); decide from the trunk-lean shots.
- wip/brain-sample: merge animal-behaviours in, or retire it now that item 14 is captured. Never merge the profiler's
  wip/brain-blocked-* branches.
- F-02 held.
