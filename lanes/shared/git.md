# Git and branches (rules 3, 11)

- Branch from origin/main as the card says. One commit per task or fix. Push your own lane as soon as it compiles,
  your checks pass and your shots are taken; then report. The supervisor reviews after the fact.
- **Ask the supervisor first for:**
  - shared or unowned files (COORDINATION.md "Lanes");
  - network tables: ghost fields, enums sent over the wire, `PlayableSpecies`, species or biome list order, SoundId;
  - anything that moves landmarks, placement or the world fingerprint (`world-data.md`);
  - `ProjectSettings/`, `Packages/`, renderer assets;
  - a merge with conflicts in another lane's files;
  - releases.
- Never push `main`, open or merge PRs, or close issues. Never rewrite pushed history: merge, don't rebase.
  Merging another lane's branch into yours is fine.
- Never write in another lane's worktree. Never bare `git stash` (the stack is shared across worktrees).
- Line endings are mixed per file: `git ls-files --eol <file>` before every `git add`; no `sed -i`; edit CRLF
  files byte for byte.
- `git status` after a build: Unity rewrites some settings files; don't commit them by accident.
- End commit messages with the session's Co-Authored-By and Claude-Session lines.
- The repo is public: no secrets, personal details or user-folder paths in anything pushed.
- A push that fails with a 500: retry once.
