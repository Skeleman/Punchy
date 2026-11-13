# How lane docs are split (rules 1, 29)

A task agent loads only what its card needs.

## Load order
1. The repo's `CLAUDE.md` loads by itself. It points to `docs/agents/<area>.md`: read only the areas you touch.
2. `COORDINATION.md`: the core (aim, lanes, rule table).
3. `lanes/<lane>/STATE.md`: where the lane stands.
4. Your card, `batches/<id>.md`.
5. Only what the card's "Read first" line names: `lanes/shared/*.md`, `lanes/<lane>/FILES.md`, `TESTING.md`, a
   DESIGN file or a section of one.
6. The task's GitHub issue (`gh issue view <n>`) only when the card says Zach's own wording matters.

Not for lanes unless a card names a section: JUDGMENT_CALLS.md, SUPERVISOR-HANDOFF.md, REFACTOR-ROADMAP.md,
ROADMAP-IDEAS.md, BENCH-*.md, other lanes' REPORTS and cards.

## Modules in `lanes/shared/`
| File | Covers | Who needs it |
|---|---|---|
| git.md | branches, pushing, the ask-first list, line endings | every card |
| report.md | handoff per row, report format | every card |
| unity.md | editor, builds, HOLD, processes | any row that runs Unity |
| checks.md | staging, shots, suite | any row with a visible result or tests |
| rendering.md | LOD by screen size, far view, lighting path, NaN | shaders, meshes, anything seen at distance |
| world-data.md | world may move, placement as records | terrain, water, landmarks, settlements |
| bench.md | who profiles, targets | rows that need a cost check; the profiler |
| design.md | details, destinations, stand-ins, refactors | behaviour, items, refactor cards |

## Writing docs (rule 29)
- **One home per fact.** A rule lives in one module; cards and other docs point to it by file or rule number and
  don't repeat it.
- **Paraphrase Zach precisely; don't paste his messages.** State the decision, its date and its limits in the
  fewest words. His own wording is kept once, on the GitHub issue.
- **No history in reference docs.** Replace, don't append; superseded text is deleted, not struck through. Git
  history is the archive. Dated logs go in REPORTS and SUPERVISOR-HANDOFF.md only.
- **Cards say what and done-when.** Background goes in a DESIGN file or the issue, named by section.

| File | Limit |
|---|---|
| COORDINATION.md | 100 lines |
| A module or STATE.md | 60 lines |
| A card | 40 lines |
| A DESIGN file | 120 lines; split by section when rows need different parts |

## Card template
```
# <id>: <title> (<lane>, #<issue>)
Tree, branch.
Read first: git.md, report.md, <other modules>, <DESIGN file sections>, <lane FILES/TESTING if needed>.
Why: <one or two lines; which aim priority it serves>.
| Row | Do | Done when |
Pins: <what must not change; asks that are the supervisor's or Zach's>.
```

## Budgets are soft
A card's call and build counts are targets. A task one fix from done may take about 25 more calls and one more
build; never start a new task in the extra budget. If one fix plus a rebuild doesn't get there, stop and report
what you ruled out, with calls and builds used.

## Where new knowledge goes
| You learned | Put it in |
|---|---|
| A lasting code or engine gotcha | the repo's `docs/agents/<area>.md`, same commit as the fix |
| How to set up, frame or count something | `lanes/<lane>/TESTING.md` |
| Where things live | `lanes/<lane>/FILES.md` |
| What's done or in flight | `lanes/<lane>/STATE.md` and the issue |
| Process for every lane | tell the supervisor |
