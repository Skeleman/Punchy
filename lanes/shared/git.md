# Git and branches

- Work on `wip/<topic>` branches off your lane branch; merge into the lane branch when the batch's checks pass,
  and push your own lane without asking. One commit per task or fix.
- Ask the supervisor first for: shared files (CLAUDE.md, GLOSSARY.md, ProjectSettings, Packages), network tables
  (ghost fields, `PlayableSpecies`, species/biome list order, SoundId), landmark placement or the world
  fingerprint, and any conflicted merge with the other lane.
- Never push `main`, never rewrite pushed history (merge, don't rebase), never write in another lane's worktree.
- Line endings: check `git ls-files --eol <file>` before `git add`; edit CRLF files byte-for-byte (no `sed -i`).
- Check `git status` after a build: Unity rewrites some settings files; don't commit them by accident.
- End commit messages with the session's Co-Authored-By and Claude-Session lines.
- Batch shell work: several greps in one call, one git call per merge step. GitHub pushes sometimes fail with a
  500: retry once.
