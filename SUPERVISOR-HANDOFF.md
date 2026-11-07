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
