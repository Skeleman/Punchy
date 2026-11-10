# Supervisor handoff, 2026-11-06 (to the player-lane session)

CANCELLED 11-06 late: Zach asked the main-account supervisor to continue. Player session: go back to the
player lane only; do not merge or review other lanes. This file stays as the handoff if the budget runs out.

Zach asked the supervisor (main account) to hand off to you when its token budget ran out. You now also
supervise. Read COORDINATION.md first (rules 1-18; rule 18 = every process ends on its own, kill by PID).

## Lanes right now
- 11-07: merged #184 (W-118), #185 (A-86), #186 (W-119), #187 (A-87), #188 (W-120), #189 (W-121), #190 (A-88).
  Pins on main: Landmarks_V3 111 3BDF705F, Fauna_Fingerprint AD7E7781. #40 closed (every biome has a colossus).
- World agent (a39709da2c1966bd8): on W-122 (marsh folk stilt village, new race).
- Animal agent (a1b9928d0d0855997): on A-89 (colossus emissive, build hitch splits, marsh wildlife).
- Main hotfix PR #181: Underwater.cs ?? on Unity fake null broke the suite (13/14); fixed and noted on P-20.
- Player: you (P-20).
- Profiler (laptop): job 23 queued in BENCH-QUEUE.md (b/fix-proc-cleanup: timeouts, kill by PID). Review + merge.
Those two agents ran as subagents of the old supervisor session; if they stop, relaunch them from their cards.

## How the supervisor works
- Review each lane's pushed rows; merge with a PR when `git merge-tree --write-tree origin/main origin/<branch>`
  is clean (Zach granted gh pr merge; agents never merge or close issues).
- Close finished issues without asking; add a row to PLAYTEST.md (LF) for each.
- Keep lanes busy with cards in batches/; check open issues and lanes/ISSUES-MAP.md first.
- Editors: one per lane, open only with >= 6 GB free, queue in EDITOR-QUEUE.md; never kill another lane's editor.
- After an editor closes, prune old Burst cache: delete Library/BurstCache files older than 7 days in that
  project only (frees GBs; Unity never prunes it).
- No releases, ProjectSettings or machine-setting changes without Zach's OK. Public repo: no secrets/paths.
- Be brief with Zach; he is low on tokens.

## Open items for Zach (morning summary)
- Laptop display question (fps cap 60 stalls); one fpscap-60 check still to run on the main PC.
- Design: 4 or 6 attributes; hunger/thirst or not; overrides file per island or per area.
- #69 close?  Refactor PRs #171-176 status.  Cave creature count risk.
- PLAYTEST rows still owed: ribcage (W-114), GiantSpider in deep caves, cave dungeon sockets, A-82.
- Card pins changed after the ribcage: Fauna_Fingerprint 8A1979F1, Landmarks 111 3BDF705F.
- Untracked GameAudio.cs in the coordination tree: not the supervisor's; check whose.

## Player-lane session, 2026-11-06 late (stood in briefly, then back to the player lane at Zach's word)
- Merged PR #178 (world-features: W-115 rows 2-3, W-110 rows 1-2, W-117 GC row; all verified in REPORTS-world) and PR #179 (player-client: P-20 incl. the GC row). merge-tree was clean for both.
- PLAYTEST.md: #39 road tunnels row added (#39 was already closed).
- Job 23 (b/fix-proc-cleanup): no SHA pushed yet, nothing to review.
- World and animal agents are offline (all peer sessions show offline). Not relaunched: Zach will decide later.

## State when the supervisor stopped (spend limit, no handoff), checked 2026-11-07 09:40 by the player-lane session
- Both lane agents stopped with it, mid-row. Nothing committed after 07:52 (world) and 07:57 (animal); their editors were left open in Play mode (world 7.3 GB, animal 5.7 GB, 3.5 GB free). Not closed: other lanes' editors (rule 14), waiting on Zach.
- World (D:\Unity\Punchy-world, world-features 05da2ec9): W-122 rows 1-2 done and verified, pushed, not merged. Row 3 (one surprising marsh folk touch) in progress: uncommitted, +59 lines in Terrain/Landmarks/StructureKit.cs; untracked Assets/Temp.
- Animal (D:\Unity\Punchy, animal-behaviours ba09c891): A-89 row 1 (colossus emissive) done and verified, pushed, not merged. Row 2 (split slow colossus builds) in progress: uncommitted StoneColossi.SaltCrystal.cs and StoneColossi.CarnivorousPlant.cs, TempleGlow.mat. Also uncommitted edits to shared files: CLAUDE.md (+3), ProjectSettings/Packages/com.unity.pipeline/EditorPipelineConfig.json, docs/agents/editor-and-assets.md, docs/agents/rendering.md. Row 3 (marsh wildlife) not started. Untracked Assets/Temp, Assets/_Recovery, Assets/Resources.meta.
- No open PRs; queue empty. Uncommitted work left in place (rule 5): the next session for each lane should commit or finish it.
- Player: waiting on an OK for the Sky.hlsl underwater-fog block (REPORTS-player 11-07). W-121 also edits Sky.hlsl haze.
- 09:50: Zach closed both editors from the GUI; their launchers ended with them, 18.7 GB free. Uncommitted work is still on disk in both trees.

## Stand-in supervisor: the player-lane session (Zach, 2026-11-07, while the main supervisor is out for two days)
- Relaunched the world agent (finish W-122 row 3) and the animal agent (finish A-89 rows 2-3), each in its own tree, from the state above. Each reports DONE/FAILED/BLOCKED at card end; the stand-in reviews and merges by PR.
- Sky.hlsl: lent to the player lane for one additive block in PunchyAtmosphere (`_PunchyUnderwater` underwater fog, set by Underwater.cs). The world lane isn't editing Sky.hlsl on W-122.
- 11-07: W-122 done (rows 1-3, last fb83c0c7), reviewed and merged as PR #192. Pins after it: Landmarks_V3 112 A955AFE0 (new MarshFolkVillage entry), Fauna_Fingerprint AEA50DCE on world-features (four new marsh flora assets; accepted). Hatchling eye glow at dusk not visible (unverified, cosmetic). World agent finished, no next card given.
- 11-07: player PR #191 (underwater fog, ??= null checks) open: the stand-in may not merge its own lane's PR; Zach to review.
- 11-07: FEATURE FREEZE (Zach). Refactor roadmap from the ChatGPT reviews in REFACTOR-ROADMAP.md; cards come from it. Animal agent told to finish A-89 row 2 and pause row 3.
- 11-07: A-89 done (rows 1-3) and merged as PR #193 (Zach kept row 3 under the freeze and accepted pins 112 A955AFE0 / 83902658, to check in playtest). Animal agent finished.
- For the supervisor: uncommitted edits in the animal tree came from the old supervisor or its agents, not Zach; left in place: CLAUDE.md 'Roadmap Ideas', 'File Encoding' notes in docs/agents/editor-and-assets.md and rendering.md, a 'Unity Editor Policy' text in editor-and-assets.md that contradicts rule 15, and TempleGlow.mat (_EMISSION dropped, _Color changed).
- 11-07: v0.13.0 prepared from main 4fea789d: play-mode suite 14/14 passed (pins 112 A955AFE0 / 83902658); release build in Builds/Windows smoke-run clean; zip (51.7 MB, no Burst debug folder) and notes ready in the player session's scratchpad. NOT published: creating the public release needs Zach (permission check).
- 11-07: v0.13.0 PUBLISHED (Zach's OK): https://github.com/Skeleman/Punchy/releases/tag/v0.13.0, tag on main 4fea789d, zip 54.2 MB. This is the baseline for the refactor's Phase 1 numbers.
- 11-07 refactor started (stand-in): phase 0 hygiene items 0.1-0.3 in PR #194; 2.1 creature snapshot double-buffered in PR #195 (suite 14/14). Both await Zach's merge (self-authored). 0.4 packages needs Zach's OK. Next: 2.2 brain preprocessing.
- 11-07: refactor 1.1 needs numbers before 2.2-2.5: -wildcap test flag merged (PR #196, main e5c88fb4). Population sweep and stress scenes queued for the profiler as BENCH-QUEUE jobs 24-25 (Zach coordinates the profiler account). Profiler job 23 (b/fix-proc-cleanup, rule 18) reviewed and merged as PR #197. 2.2 (brain prep) waits on job 24: it already runs in Burst and brain wait is about 0.02 ms at today's population.
- 11-07: refactor 3.5a/b (static flora variant) on refactor/flora-static 5febffdb, unmerged until profiler job 26 (A/B) so jobs 24-25 keep the e5c88fb4 baseline.
- 11-07: refactor 3.2 (fire instance buffer) on refactor/fire-instances 03783ca9, unmerged until profiler job 27 (A/B).
- 11-07: Ember Forest flame gap fixed on refactor/fire-budgets (on top of fire-instances); job 27 now A/Bs that branch.
- 11-07: playtest fixes merged: tent shadow striping (PR #198, cached far shadow slope bias), tent leather grid (PR #199). Black rooftops not reproduced.
- 11-07: 3.6 mesa arches match the desert (PR #200); Fauna_Fingerprint pin now F0EF4BD5 (new flora rules field, no placement change).
- 11-07: profiler jobs 24-27 reviewed. Fire (3.2) merged #201, bench runner #202; static flora (3.5b) NOT merged (no measurable gain). New order in REFACTOR-ROADMAP.md: 3.4 water, 2.3 pose rate, 3.1 lights, 3.5c, 3.3.
- 11-07: static flora reconsidered for bigger cities (Zach): grouping done; bake held (plot buildings don't run weathering); job 28 queued (grouping A/B, bake ceiling, first-approach hitch, cache redraw cost).

## Refactor session on the other account (Zach, 2026-11-08, picked up from the player-lane stand-in)
- Playtest build of every overnight branch: `test/playtest-1108` 2b1f9fdf (pushed for audit only, never to merge). It holds water/river-flow, which has no branch of its own on origin. Zach's feedback: FEEDBACK-2026-11-08.md. ChatGPT's audit of it: `docs/Chat audit of first pass of updates.md` in the animal tree.
- Profiler freeze: main's game code is identical to 346cf4d9 (the clean job 29/30 build), so the trigger is in the bench runner or setup. Zach told the profiler agent directly.
- Merged at Zach's word: #205 static flora (3.5a/b; suite 14/14 on the merge), #206 removes Visual Scripting, AI Navigation and Analytics (0.4; suite 14/14). main d1ffc1e2.
- Proposed next (after the audit): the shared detail policy (3.7) first, starting with creatures; then the water quality policy (3.4); then swells pass 2 (bounds padding from the audit, plus the playtest notes). Zach: go ahead; light sources categorised in the policy too; worker lanes allowed.
- PR #207 (refactor/detail-policy, open, awaiting Zach's merge): Settings/DetailPolicy.cs (on-screen size rule: mesh, animation, shadow decided apart, with hysteresis) and LightPolicy.cs (lights by role: celestial, sky flash, carried, fixture, fire, creature, ambient glow); creatures wired (supersedes refactor/pose-by-size); far fliers and swimmers now coast between brain thinks (serpent jitter). Suite 14/14.
- Lanes started 11-08 (subagents of this session, both branch from refactor/detail-policy; never merge):
  - Water lane, D:\Unity\Punchy-world, branch water/overhaul: the overnight water branches combined; bounds padding, one wave table, swells pass 2, water quality policy plus a Reflection quality setting, faster surfacing, low-angle layering (FEEDBACK-2026-11-08 items 1-8).
  - Light lane, D:\Unity\Punchy-release, branch lights/manager: the light manager (3.1) on LightPolicy over the glow list and real lights, campfires get light, remote lanterns capped, blue moon as a strong additive light, streaks tapered (items 9-10).
- Both lanes finished (suite 14/14 each, pins unchanged), reviewed (scope, allocations; the two branches merge cleanly together) and opened as PRs stacked on #207: #208 lights/manager, #209 water/overhaul. None merged: Zach merges #207 first, then these. Combined local build of all three: test/playtest-1108b (player tree, Builds/Playtest2).

## Handoff back to the main account, 2026-11-08 evening (from the refactor session on the other account)
Read first: REFACTOR-ROADMAP.md (order and status), FEEDBACK-2026-11-08.md (Zach's playtest notes, items 1-24), the section above.
- main 4669aabc: #205 static flora, #206 packages, #207 detail policy (3.7: Settings/DetailPolicy.cs, LightPolicy.cs; creatures wired; serpent jitter fix) all merged by Zach. Pins on main: Landmarks_V3 112 A955AFE0, Fauna_Fingerprint F0EF4BD5.
- Open, waiting on Zach's merge (both merge-tree clean on main 4669aabc and with each other; base still refactor/detail-policy, retarget to main before merging):
  - #208 lights/manager 6f6ae2ad (3.1 light manager on LightPolicy over the glow list and real lights; campfires lit; remote lanterns capped; blue moon additive light; streaks fainter, bluer, half speed; puddles absorb on sand, only on level ground or hollows; `-place meadow` no longer locks the camera). Items 9-10, 22-24.
  - #209 water/overhaul bc49b74d (3.4 water quality policy + Reflection setting; bounds padding; one wave table; swells pass 2 with finer detail and shore-facing direction; floaters ride the swell; weather hidden under water; faster surfacing; low-angle layering; lava redone as flow noise with veins in a dark crust, written from the technique, not the CC BY-NC-SA shadertoy code). Items 1-8, 18-21. Known cost: swimmers and floaters skip pose reuse (noted on the PR).
- Playtest builds (player tree, local branches only): Builds/Playtest2 = test/playtest-1108b (round 1); Builds/Playtest3 = test/playtest-1108c e01718e8 (#207 + #208 + #209 round 2; suite 14/14, built 16:48). Zach has not playtested Playtest3 yet.
- Lanes: water lane (D:\Unity\Punchy-world, water/overhaul) and light lane (D:\Unity\Punchy-release, lights/manager) are finished and idle; both were subagents of the other account's session and will not resume. Relaunch fresh from a card if more work is needed. No editors open.
- Open bugs/notes: jagged white sheet at `-place SeaFolkLanding` (water lane, not fixed). Profiler freeze is in the bench runner, not main's game code (Zach told the profiler).
- Next, in order: (1) Zach playtests Playtest3, merges #208 then #209 (run the suite on main after each); (2) flora per-class LOD and shadow rules on DetailPolicy (3.5c / 3.3); salt ball shadow via the 3.7 shadow tier replaces FarShadow (item 17); (3) unassigned feedback: flora wind reading the grass gusts and the inflating sway (11-12), V3 colour sync for flora and grass (13), cumulonimbus shape (14), weather name notice (15), breaking crests near shore (3), SeaFolkLanding seam.
- Housekeeping: FEEDBACK-2026-11-08.md and this section are uncommitted in the coordination tree (commit them, LF). Untracked GameAudio.cs here is not this session's. The animal tree (D:\Unity\Punchy) still has the old supervisor's uncommitted edits: don't commit them blind. Untracked Assets/Resources.meta in the player tree is harmless.
- Build recipe: copy Tools/bench/BProfileTools.cs into Assets/Game/Scripts/Editor, `-executeMethod BProfileTools.Build -out Builds/X/Punchy.exe`, then remove it and `git checkout -- ProjectSettings`. Suite: `UNITY_EXE=... scripts/run-playmode-tests.sh` (14 tests).

## Main account, 2026-11-08 night
- Open for Zach: #208, #209, #210 (flora by class, plant wind, V3 greens; W-123). `sky/cost` (cloud march strides, job 34 timing) and `render/ssao-source` (`-ssao` test flag, job 35) have no PRs yet.
- Playtest build of all four: Builds/Playtest4 in the player tree = local branch test/playtest-1108d e09eb33d (suite 14/14; smoke-run clean). Never to merge. Sky.shader needs both the underwater early-out (#209) and the test mask (sky/cost): that merge had one conflict.

## Other account again, 2026-11-08 late (main account out of tokens)
- Round 3 (FEEDBACK items 25-37) given to three lanes, subagents of this session: W-124 flora (Punchy-world, flora/round3 on #210), W-125 water (Punchy-release, water/round3 on #209), P-21 lights/sky (Punchy-player, lights/round3 on #208). Item 34's fish, coral and shells are new content (freeze): ideas only.
- The main account left uncommitted roof edits on top of #211 (normal flip by the sheet alone, underside sky 0.18 -> 0.32), unverified: saved as local branch wip/roof-silhouette-2 45f5f492 in the player tree, not pushed. Include in the next playtest build for Zach to judge.
- Open for Zach: #208, #209, #210, #211 (all merge-tree clean on main 4669aabc); sky/cost and render/ssao-source have no PRs.

## Main account, 2026-11-08 ~23:45 (for whichever session builds next)
- Zach merged #208-#211 by way of the main account: main 13cdbcdc. **The suite hasn't run on this main** (every tree free of a lane was in use); run it first chance. The closest check is test/playtest-1108d (#208+#209+#210+sky/cost, 14/14).
- **Zach wants the tufts and gusts in the next playtest build:** merge `grass/fields` cee1cc04 (main + grass/clumps + grass/far-gusts, the one conflict resolved; not compiled, no PR). Not to be left out again.
- Roof: #211 as merged still has the camera-dependent underside. wip/roof-silhouette-2 (flip by the sheet alone, underside sky 0.32) showed no visible change in a side-by-side at -goto -3277,129.9,1549 (undersides still near black at low camera heights), so it needs more work, not just merging. Zach's report: a face changes colour with the camera; undersides quite dark.
- Meadow (item 25): closed. Zach: his misreading of medium-range shadow aliasing on the grass.
- Job 34: sky/cost saves 3.4 ms GPU at the Summit against main, no PR yet; Sky.shader needs the merge resolved as in test/playtest-1108d. Job 35: the prepass is forced depth priming, not SSAO's.

## Handoff to the main account, 2026-11-08 ~23:30 (other account out of tokens; it becomes one lane when Zach says)
- Round-3 lanes told to wrap up: finish or park the row in hand, write their REPORTS row, close their editor, stop. They're subagents of the other account and won't resume; relaunch from the card (rows left) if needed. Check each tree for uncommitted parked work (rule 5) and that no editor is left open.
- Progress when told to stop (pushed branches; see REPORTS-world / -water / -player for the latest):
  - W-124 flora (Punchy-world, flora/round3 on #210): row 1 (item 25) CLOSED, not a bug: mid-range shadow aliasing Zach misread. Row 2 (26, stems rooted) done 17bd5157. Rows 3 (29, cull ignores height) and 4 (27-28, wet gloss, bark) in progress or not started.
  - W-125 water (Punchy-release, water/round3 on #209): row 1 lava orange/black 30% speed (gold kept as a variant) 8bf99541; row 2 per-fluid under-water look 2960a93d. Rows 3 (32 storm wind drives swell), 4 (34 bubbles/motes; fish/coral ideas only), 5 (38 light shafts, fog darker with depth, added by Zach) left.
  - P-21 lights (Punchy-player, lights/round3 on #208): row 1 blue moon deep blue + fog/clouds a9ad8fdf; row 2 clear-day fog 884a4673. Row 3 (31 interior light leaking outside) left.
- Not assigned: mid-range shadow aliasing/shimmer (Zach, item 25 follow-up; related to the far tree shadow "glitter" in the roadmap).
- Still open for Zach: #208-#211; roof WIP on local branch wip/roof-silhouette-2 45f5f492 (player tree, unpushed, unverified). No PRs or Playtest5 yet for round 3: next supervisor reviews, opens PRs (stacked on #208/#209/#210), builds Playtest5.
- P-21 final: rows 1-2 pushed (lights/round3 884a4673). Row 3 (item 31: window lights become indoor-only Fixture lights, no glow slot) works in shots but is PARKED staged/uncommitted in the player tree (LightBudget.cs, LightPolicy.cs, CaveSystem.cs, Glow.hlsl): run the suite, then commit and push. Merge-tree with row 3 clean vs sky/cost and water/overhaul. Details in REPORTS-player.
- W-125 final: rows 1-4 pushed (water/round3 a982f73e: lava with gold kept behind _GoldLiquid, per-fluid look, swell from weather wind, bubbles/specks). Row 5 (item 38 light shafts, fog darker with depth) committed LOCAL ONLY in the release tree, 696e7ee0: measure GPU per quality, run the suite (not run for any row), then push. Merge-tree: clean vs lights/manager; one Sky.shader conflict vs sky/cost (same early-out one as #209). Details in REPORTS-water.
- W-124 final: row 1 closed (not a bug); rows 2-3 pushed (flora/round3 7a78a5ad: stems rooted via flutter by height; cull uses 3D distance, no hole under a high camera). Row 4 (items 27-28: bark grain, less wet gloss) PARKED uncommitted in the world tree's Flora.shader, unverified: re-shoot rain in sun and a trunk close-up, A/B GPU, commit, push. Suite not run this card. Details in REPORTS-world.
- All three round-3 lanes stopped; no editors left open. Owed by the next supervisor: the suite on each branch, the parked work above, then PRs and Playtest5.

## Main account supervises again, 2026-11-09 (Zach)
- Zach: this session supervises and merges; feature freeze lifted; profiler only through BENCH-QUEUE.md (other account). Zach will ask the previous supervisor for its handoff.
- Playtest9 (player tree Builds/Playtest9, local test/playtest-1110 821a9752) holds every pending branch.
- Merging now (suite on candidate main+5 = 2103139f): lights/round3, detail/four-rules (R-2), sky/lowres (R-1; job 36 laptop numbers still to come), fix/bloom-clamp, grass/fields.
- Held for Zach's Playtest9 notes: water/round3 (last commit, shafts, unbuilt by the lane; Sky.hlsl/Sky.shader conflict with sky/lowres, resolved in test/playtest-1110) and flora/round3 (bark row parked unverified).

## Status from the outgoing supervisor (now back on the player lane), 2026-11-09
- **The latest build is Playtest9, not Playtest5.** Builds/Playtest9 in the player tree is local test/playtest-1110 821a9752: origin/main 13cdbcdc plus flora/round3, water/round3, lights/round3, grass/fields, sky/lowres (includes sky/cost), fix/bloom-clamp and detail/four-rules. Suite 14/14 on that exact tree; a 60-second smoke run had no exceptions or shader errors. Zach has not reported on it yet. Playtests 4-8 are superseded.
- **PRs are open, none merged.** Zach asked for these to be merged, but the session's permission check refused the merge step, so only the PRs exist: #212 flora/round3, #213 water/round3, #214 lights/round3, #215 grass/fields, #216 fix/bloom-clamp, #217 sky/lowres, #218 detail/four-rules. Main is still 13cdbcdc.
- #214-#218 are the five in the merge plan above; use them rather than opening new ones. #212 and #213 are the two held for Zach's Playtest9 notes: do not merge them until then. The PRs were opened before the plan above was seen, and can be closed if unwanted.
- All seven report mergeable one at a time; conflicts appear as they stack. Resolutions are in test/playtest-1110: Sky.hlsl keeps the underwater shafts function and `_PunchyHazeClear`; Sky.shader keeps the underwater early-out with shafts, then the test mask and the low-resolution cloud branch. If all seven land, main's tree should match test/playtest-1110.
- Not verified in Playtest9: flame flicker in continuous flight (stills only, at least 0.5 s apart); underwater shafts never seen on screen or timed; lava glow unseen (0 lava texels on this island); fire glow brightness is a first value; world glow on host and joined client, and its bake time on the laptop; bark and gloss look. The blue-moon ground came out very saturated in one test shot; it may be too strong.
- Still open: job 36 (sky/lowres laptop timing); roof undersides still dark and view-dependent (wip/roof-silhouette-2 45f5f492, local to the player tree, showed no improvement and is in no build); mid-range shadow aliasing; depth-priming investigation; 3.7 step 3 shadow tiers; W6 and W7.
- Do not merge: test/playtest-1109, test/playtest-1110, water/fine-grid.
- No lanes, editors or players from the outgoing supervisor are running. The player tree is still on test/playtest-1110 and moves to a fresh branch from origin/main with the next player-lane task.
- 11-09 14:41: merged #214 lights/round3, #217 sky/lowres, #216 fix/bloom-clamp, #215 grass/fields, #218 detail/four-rules (main merged into the branch first: GitHub saw two merge bases as a conflict). main acd0cad5 has the same tree as the suite candidate 2103139f: 14/14, pins 112 A955AFE0 / F0EF4BD5. PRs #212 flora/round3 and #213 water/round3 stay open for Zach's Playtest9 notes.
- 11-09: merged #219 flora/habitat (W-126) -> main 5780c3f7. Fauna_Fingerprint pin is now 7D75BB04 (by design); Landmarks_V3 112 A955AFE0. Suite 15 + 4 Explicit (SKIP), 210 s. Framed real-render shots not taken (batch renders show placeholder trees): judge in the next playtest. Cards queued: P-22 (player account), R-4 after R-3; bench job 38.
- 11-09: merged #220 shadow/tiers (R-3 rows 1,3,4) -> main 5558f89a. Row 2 is card R-5 (after R-4). Bench job 39 (depth priming on the laptop). R-4 started.
- 11-09: merged #221 sky/look (R-4) -> main 0e964452. Skybox full-res march compiled out under PUNCHY_CLOUDS_LOW_RES; laptop check is bench job 40. Unverified: overcast look, cirrus amount, leaf-edge leak. Added COORDINATION rule 19 (lean suite, no rework). Next in the rendering lane: T-1 screenshot tool, then R-5. W-127 town lights running in Punchy-release.
11-09: merged #222 town lights (W-127) -> main ff1e881d; Fauna_Fingerprint now B15790ED, Landmarks_V3 unchanged. W-128 started in Punchy-release. T-1 rows 3-4 in tests.
11-09: merged #223 T-1 shot tool -> main 2ba4ec56. R-5 started in Punchy-world. Next: job 41 result -> card a stall fix; W-128 report.
- 11-09: merged #224 shadow/wide (R-5 row 1) -> main f46a65f9. Row 2 (FarShadow retirement) on local Punchy-world branch r5-row2-wip ac1b326e: blocked by untracked WorldProbe.cs using FarShadow; asked Zach. R-6 (lod/big) started in Punchy-world; R-5 side notes folded into R-6.
- 11-09: Zach OK'd trimming WorldProbe; removed its FarShadow probe (untracked, Punchy-world). R-6 agent cherry-picks ac1b326e onto lod/big as 'R-5 row 2'.
- 11-09 evening: Zach will install more RAM. Pause after R-6 (WideShadow destroyed-mesh fix + shrub streaming numbers) finishes: no new agents or editors; make sure all Unity editors are closed. Queued for after: white-splotch card (pre-bloom NaN/overbright debug view), W-128 merge awaits Zach on the marsh village move.
- 11-09 late: R-6 done on lod/big 809f8469 (includes WideShadow stale-caster fix; main has that bug since R-5). NOT merged: row 4 (shrub far reach) costs +3.3-3.8 ms mean frame (~45%), no extra hitches, +2.6 MB. After Zach's RAM install: cap shrub far reach (600 m) or revert row 4, re-run suite, then merge. All editors closed; paused.

- 11-09: Zach installed RAM (63 GB visible, ~54 GB free idle). Pause lifted. Running: W-128 row 5 (marsh revert) in Punchy-release, R-6 row 6 (shrub reach back, suite incl. WideShadow fix) in Punchy-world. Next: merge both, then impostor card, A-90, white-splotch card (need Zach shot/location).
- 11-09: W-128 merged as #225 (main adb548d7): marsh move reverted, Fauna_Fingerprint re-pinned 66320C7C (kit inn anchors), Landmarks_V3 unchanged. FolkTalk WalkBy_NeverThroughWall flaked once (passed alone); watch it. R-6 must merge origin/main (new pin) before its PR.

- 11-09 late: R-6 merged as #227 (main 1846211d): size-on-screen LOD, trees-only far reach (shrubs back to radius, walk +2.7 ms gone), WideShadow stale-mesh fix. Suite 15/0/7, pins 112 A955AFE0 / 66320C7C. R-7 impostors started on lod/impostors (world tree). Job 42 (swapchain stall fix A/B) queued for the profiler.
- 11-09 late: running R-7 (Punchy-world), R-8 (Punchy-release), W-129 (Punchy). Queued: A-92 in Punchy after W-129; R-9 in the first free render tree after R-7/R-8. Drafts awaiting Zach: P-23, P-24, A-91. P-22 merged (#229).
- 11-09: P-25 assigned to the player ACCOUNT (Zach: spread tokens), via lanes/player/STATE.md; player-lane cards go to that account, not supervisor subagents. R-10 queued after R-8.

- 11-09: NPC talk split (Zach: animal-related goes to the animal lane): A-93 (animal, after A-92: FolkPersona, FolkConversation API, lines, animal-player reactions; row 1 merge early) -> P-26 talk window, P-27 stats (player account, after P-25).
- 11-10: W-129 done, no code (editor stalls under host load, no repeatable streaming spike); bench job 44 queued to confirm in a player build. A-92 started in D:/Unity/Punchy (fauna/detail).
- 11-10: R-7 merged #233; R-7 row 5 (atlas trim, lod/impostor-trim) sent to the same agent in Punchy-world.
- 11-10: R-8 rows 1-2 pass (skybox -54% noon, -39% night); row 3 missed 15% (clean -7.5% storm); chose (a) commit clean, speckle rejected; (c) march savings elsewhere = possible later card. Merge sky/cost after its row 3 commit, then R-10 in Punchy-release.
- 11-10: R-7 row 5 merged #234 (atlas 140 -> 39 MB). Punchy-world free -> R-9 water/deform started there.
  To check: magenta (missing-shader) props on the R-7 hilltop view (PunchyMilestones/R-7/r5/before/hilltop-imp.png), pre-date R-7; owner unknown.
- 11-10: R-8 merged #235 (rows 1-3). R-10 started in Punchy-release (light/forwardplus): row 2 does Flora + built first; creature + water shaders wait for A-92 / R-9 merges (tell the R-10 agent when they land). W-130 (magenta slabs) is a side task of the R-9 agent.
- 11-10: W-130 merged #236 (magenta slabs = R-7 impostors failing to compile on D3D11). Any build made from main between #233 and #236 has magenta far shrubs.
- 11-10: A-92 resumed for row 3 (it stopped mid-suite). A-90 was already merged (#226, 11-09; flier no-trail shot still unverified); Punchy-world free; R-9 waits on bench job 45 (water/deform branch kept).
- 11-10: R-11 (far trees and rocks on impostors, R-7 row 4 never done) started in Punchy-world on lod/impostor-trees.
- 11-10: A-92 merged #237 (main a975f71e); #230/#231 closed. Asked Zach on the detail defaults. R-10 row 2b may now hook up Creature (R-9 still pending). D:/Unity/Punchy tree free; burst-prune when editors close.
- 11-10: A-93 (folk personas/conversation) started in D:/Unity/Punchy on fauna/persona; merge row 1 early so the player account can start P-26.
- 11-10: A-93 merged #238 (main 18df736a). P-26 unblocked in lanes/player/STATE.md. D:/Unity/Punchy tree free.
- 11-10: R-11 merged #239 (main da39fd72). Row 4 list: far kit buildings best next (needs lit-window mask, ask R-10 first); landmarks not worth it. Punchy-world tree free.
- 11-10: W-131 started (Punchy-world, far terrain ring for flight). A-94 started (Punchy, breath follow-up).
- 11-10: A-94 merged #240. D:/Unity/Punchy tree free.
- 11-10: A-95 started (Punchy, dungeon collision = A-91 row 1; rows 2-4 still await Zach).
- 11-10: R-10 row 4 done (95c93aef, town map + light dome, desktop pass). HOLD merge for laptop job 45 (spec updated to 95c93aef, 4th arm -townglow 0; jobs 43/44 ahead). Water row 2b waits on R-9. Opened #241 (lavender squares + pill cloud on main); R-12 started in Punchy-release.
- 11-10: Zach: town light dome far too strong, lights too much sky; wants glow concentrated where the town is. R-12 paused to free Punchy-release; R-10 resumes for row 4b (dome way down, glow on the town).
- 11-10: R-12 paused cleanly (notes in its card, nothing committed). R-10 resumed for row 4b (dome way down, glow on the town).
- 11-10: Zach likes the A-92 fur defaults and the A-94 mammoth breath (both stay). New: A-96 rabbit white sparkles (queued after
  A-95 in D:/Unity/Punchy), M-05 Model Lab draft (Zach to pick in-game vs editor window, and which family first).
- 11-10: Zach approved P-23 (gear has stats), P-24 (menu-only tracker, saved progress robust to world changes), A-91 rows 2-4
  (dungeons ~4x, caves ~2x with 1-3 gentle main paths, vertical side chambers OK). Cards updated; DRAFT marks removed.
- 11-10: W-131 reviewed (world/far-ring 94548d6f, merge-tree clean; ring to ~2.97 km at High, +0.09..+0.35 ms; row 3 reach not kept,
  invisible). Held for row 2b soak (possible disc-chunk leak after preset switches). Suite fail LiveFolk_Answer = shared PlayerPrefs
  body=Crow, test fix added to A-96 row 0. Empty untracked GameAudio.cs (11-04) in coordination: harmless, left.
- 11-10: W-131 soak flat (no leak), merged #242 (main ef393625). Punchy-world tree free. Next world item: none queued; R-12 could move here if R-10 runs long.
- 11-10: R-10 row 4b done (0a4b3b50): dome off by default (`-towndome 1`, small halo), 296 window/street-light sprites, town map 0.35. Within noise vs off. Sheets sent to Zach for OK. Job 45 re-pointed to 0a4b3b50. Merge after Zach OK + job 45. Punchy-release free.
- 11-10: Zach likes the 4b town look; wants it a little dimmer and one far-light rule so the Ember Forest matches. R-10 row 4c added; runs in the first free tree (all three busy: A-95 Punchy, R-12 Punchy-world, A-96 Punchy-release). Merge R-10 after 4c + job 45 (re-point job 45 to the 4c commit).
- 11-10: Zach asked for per-segment surface tags (no fur on hooves, wet eyes, scaly lizards, flat horn/tusk/antler) and non-shoe wolf paws: card A-97, queued after A-96 (shares Creature.shader).
- 11-10: A-95 rows 1-2 merged (#243, 06b2a72b). Temple hash on the card was stale (9C11B352 = pre-P-9); real 81129C52 unchanged. Leftovers: camera clips in caves (player lane), one temple socket in 0.6 m crawl space (player data). Animal lane next: A-91 rows 2-4 in D:/Unity/Punchy; A-97 after A-96.
- 11-10: A-91 rows 2-4 started in D:/Unity/Punchy (fauna/dungeons2). A-96 row 1 found: fur bump ddx/ddy inside a branch -> NaN on capsule pole slivers, bloom spreads it. Also 229 non-creature NaN pixels at the horizon/mountain edge (sky or terrain): needs its own card after R-12.
- 11-10: Zach: all LOD by screen size (DetailPolicy.ScreenSize), no fixed metres. Rule 20 in COORDINATION; A-97 updated; R-13 card (convert the rest) queued after R-12/R-10 4c.
- 11-10: R-12 row 1 reported (partial): squares only on light/forwardplus, never on main -> added a squares check to R-10 4c. Cloud is on main, commit not found; bisect Sky.hlsl in one session with the weather pinned (R-12 row 1b, after R-10 4c). LiveFolk_Answer fails on main (fauna).
- 11-10: Zach: pill clouds seen before, likely original cloud code. R-12 row 1b = read/fix cloud shape code, no bisect; runs in Punchy-world after R-10 4c.
- 11-10: A-96 merged #244 (ed0e68ef). A-97 started in Punchy-release. Non-creature horizon NaN (229 px wolf noon) = small card later (R lane, after R-12).
- 11-10: Zach: cheaper far ground + thinner fog + sunset horizon oddity -> W-132 card (first free tree), BENCH-QUEUE job 46 ON HOLD until W-132 rows 2-4 push.
- 11-10: Zach: M-05 approved as a Unity editor window (editor code only), creatures first; A-98 underwater sound (muffle + pitch + hum, closes #22) queued in the first free fauna tree; grass shimmer dropped. W-124 shots no longer on disk (only W-125's); sent W-125 sheets for #212/#213 reminder.
