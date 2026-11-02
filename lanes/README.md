# Lane briefs (modular)

A task agent loads only what its batch needs, so a lane can know a lot without every agent reading all of it.

## Load order for a task agent
1. The repo's `CLAUDE.md` loads by itself. It points to `docs/agents/<area>.md`: read those only for areas you touch.
2. `lanes/<lane>/STATE.md`: where the lane stands (branches, what's in flight). Always read.
3. Your batch card, `batches/<id>.md`: the tasks, with the issue numbers and the modules each one needs.
4. Only the modules your card lists: `lanes/shared/*.md`, `lanes/<lane>/FILES.md` (key files),
   `lanes/<lane>/TESTING.md` (how to set up and check things in isolation).
5. Each task's GitHub issue (`gh issue view <n>`) for Zach's wording and history.

## Size limits (keep them, or the point is lost)
| File | Limit | Rule |
|---|---|---|
| STATE.md | 60 lines | Replace, don't append. Old state lives in git history. |
| A module | 120 lines | Split it by topic when it grows. |
| A batch card | 40 lines | Tasks point to issues and modules; no pasted background. |

## Budgets are soft
The card's call count and build count are targets, not walls. A fresh agent re-reads everything,
so finishing in place is cheaper than a follow-up batch.
- A task that is one fix away from done (a number just off target, a reshoot, a small bug):
  spend up to about 25 more calls and one more build to finish it.
- Never start a new task, or a task you dropped, in the extra budget.
- If one fix plus a rebuild still doesn't get it there, stop and report what you ruled out.
- Report the overrun in one line: calls used, builds used, and why.

## Where new knowledge goes
| You learned | Put it in |
|---|---|
| A lasting code or engine gotcha | the repo's `docs/agents/<area>.md` (same commit as the fix) |
| How to set up, frame or measure something | `lanes/<lane>/TESTING.md` |
| Where things live | `lanes/<lane>/FILES.md` |
| What's done or in flight | `lanes/<lane>/STATE.md` and the issue |
| Process for every lane | `lanes/shared/` (ask the supervisor first) |

## Finishing a batch
Finish your own runs first, then: update STATE.md, comment on each issue, add one report entry
(`lanes/shared/report.md`), commit to `coordination`, and stop. Don't wait around or poll for replies.
