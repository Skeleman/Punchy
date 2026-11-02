# Animal lane: state (2026-11-02, after batch A-29b)

Worktree `Punchy` (animal lane only). On `animal-behaviours` = origin de2e8e0 (A-29 + A-29b), which sits on
70737ed (origin/main with PR #52 merged in), so the lane now defaults to v2 with no flag. Status clean apart from
the old untracked `Assets/_Recovery/` (not ours). Editor on port 7800, 0 errors.

## Folk jobs and role lines (A-29, A-29b)
- `Fauna/FolkJobs.cs`: FolkJob enum (Baker, Healer, Smith, Gatekeeper, Woodcutter, WellKeeper, Guard, Merchant,
  Herder, Fisher, Farmer, Trader, plus bat-folk RoostKeeper, Tanner, Forager, PoolFisher). FromTask maps a
  resident's task to a job, Of reads it, Mood picks Plain/Cheerful/Grumpy, VillagePosts is the post table,
  BatJob plans bat-folk chamber jobs.
- CreatureData has `byte Job`. It is local, seed-derived state (not a ghost field): a joined client works out the
  job the task implies. If jobs ever need to differ from the task, add Job to CreatureGhost.
- FaunaDirector hands out village posts and logs one `FolkJobs:` line per village (job counts plus free folk)
  and one per bat-folk chamber (planned jobs).
- FolkTalkSystem.RoleLine: when a working folk talks, it says a line from its job's pool (DialogueArchive.PickRole,
  tagged by mood) and logs `FolkLine: <Job> (<Race>, <Mood>): "<line>"`. dialogue.json has 251 lines.
- `-watch Human:<Job>` (DebugControls) points the camera at a folk with that job.
- A-29b fixes: (1) DebugControls cached landmark locations from the baked v1 island, so in the editor `-place`
  went to v1 spots; the cache now resets when the landmark set changes (count + first position). (2) The bat
  chamber line never printed because the v1 blob (0 sites) set a static flag first; it now uses a per-island key.
- Herder, Fisher, Farmer and Trader did not come up in the villages checked (no matching anchors there).

## Open
- Build shot 1 (A-28, tall elder) has the bubble touching his head top; raising it relative to body height is
  still optional.
- Two-minute gaps between talks remain. Changing the rarity limits needs Zach's OK.
- Folk talks are host-only in detail (Partner/ChatRole); clients see the Stage byte (gestures and bubble).
- Town names in FolkChat lines (e.g. Muckheap for HumanVillage) look odd; they are not from this lane.

## Testing recipe
- Editor unattended Play: write launch args to `Temp/PlayArgs.txt`, enter Play over the CLI, read the console
  through UnityEditor.LogEntries reflection (Temp/a29b_log.cs filters FolkJobs/FolkLine/FolkChat). Delete
  PlayArgs.txt afterwards. The baked v1 island loads first, then v2 replaces it.
- Build shots: shotlist lines like `-place HumanVillage -time 10 -weather Clear -watch Human:Smith -wait 20` for a
  worker, `-chatshot` for a talk. `-quit` ends after the list.
