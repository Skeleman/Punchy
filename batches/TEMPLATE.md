# Batch <id>: <lane>, <date>

Agent: <lane> task agent. Worktree: <tree>. Base: <lane branch @ hash>.
Read: lanes/<lane>/STATE.md, then the modules listed per task. Stop when the card is done or blocked.
Budget: about 60 tool calls. If you get near it, finish the current task, write STATE.md and the report, stop.

| # | Issue | Task (one line) | Modules | Done when |
|---|---|---|---|---|
| 1 | #n | ... | docs/agents/x.md, lanes/<lane>/TESTING.md#y, lanes/shared/checks.md | measurable check |

Order: tasks sharing a build or shot list run together; one player session for all shots.
Out of scope: anything not in the table. Note new findings in the report's NEXT line instead.
