# Report format (one per batch)

Newest first in `REPORTS-<lane>.md` (LF). The supervisor reads this instead of looking at images.

```
## <date> batch <id>: <one-line summary>
- DONE: #<issue> <what changed for the player> (<branch> <hash>)   one line per task
- NOT DONE: #<issue> <why, in one line>
- Checks: compile / build / exceptions / numbers. Say what you checked, not just "works".
- Shots: <path> (<= 3, each with a one-line caption). Leave this out for non-visual changes.
- Learned: <file you updated, if any>
- NEXT: <what the next batch for this lane should take>
```

- No progress messages mid-batch. Message the supervisor only if you're blocked on a decision that's Zach's or
  on a shared file.
- Numbers beat adjectives: frame times, counts, distances, before/after.
