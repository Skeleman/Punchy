# Reports and handoffs (rules 4, 16)

**Hand off after every row.** As soon as a row is finished and verified: commit and push it, append the entry
below to `REPORTS-<lane>.md` (LF, newest first) and push that. Do it before starting the next row, so a usage
limit never loses work. On the main account the `handoff` skill does all of it.

```
## <date> <card> row <n>: <one-line summary>
- DONE: #<issue> <what changed for the player> (<branch> <hash>)
- NOT DONE / UNVERIFIED: #<issue> <why, in one line>
- Checks: suite line (passed / failed / skipped, seconds), probes, numbers. Say what you checked
- Shots: <path> (<= 3, one-line caption each). Leave out for changes you can't see
- Asks: BENCH request, rule 11 asks, old code noticed (rules 20, 22, 28)
- NEXT: <next row>
```

- No progress messages mid-row. Message the supervisor only when blocked on a decision or a shared file.
- Numbers beat adjectives: counts, distances, before and after. No timing (rule 21).
- The supervisor reads the report instead of opening images, so write what you looked for and what you saw.
