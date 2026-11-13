# Editor, assets and the repo

Read before editing assets or scenes, driving the editor from the CLI, or committing.

- Serialized values in scenes and assets override code defaults: changing a default in code doesn't change
  existing assets.
- Reimporting an asset during play mode (e.g. `AssetDatabase.ImportAsset` on a shader) rebakes the subscene and
  sends the player back to Start. Probe scripts should edit shaders while the editor is stopped.
- Editing a landmark type re-bakes the terrain (its shape is baked), unlike plant and animal assets.
- MonoBehaviour authoring classes must live in a file named after the class, or subscene baking silently skips
  them.

## Learned in October 2026 (from the lane handoffs)

- **Line endings are mixed per file.** Git Bash `sed -i` silently turns CRLF files into LF. Edit with byte-level
  Python replacements (or the editor tools) and check `git ls-files --eol <file>` before every `git add`.
- `EditorApplication.delayCall` never fires in an idle, unfocused editor: call `BuildPipeline.BuildPlayer` (or the
  action) directly in `unity command eval`, write results to a file under `Temp/` and poll it (the CLI times out
  after 30 s, but the work carries on). That drop makes the editor's pipeline server log 2 errors mid-build, which
  the build summary counts ("Succeeded 2 errors"); pass `--timeout 600` to the eval for builds and the count is 0.
- Switching commits with the editor open raises a modal "scene modified externally" dialog that blocks the CLI.
  Close the editor before switching commits.
- Unity's capture command saves inside the project (`Assets/Temp/...`): copy the shots out, then delete
  `Assets/Temp` and `Assets/Temp.meta` before committing.
- A shot that moves `Camera.main` and calls `cam.Render()` in the same eval shows no plants, rocks or ruins near the
  new pose, only their shadows: FloraSystem culls in LateUpdate for the pose the camera had that frame, and the shadow
  cache doesn't depend on the view. Move the camera, let a frame pass (a second eval), then render (W-108).
- An editor started from the CLI can open with no scene loaded: entering Play mode then builds no world, and probes
  wait forever. Open it first: `EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity")` (W-138).
- An editor killed while in Play mode leaves `Temp/__Backupscenes`, and the next start sits at a modal "Recovering
  Scene Backups" dialog that the CLI can't see (100 MB, no log). Delete that folder before restarting (W-108).
- Closing Unity: if a "scene modified" dialog appears, save or discard through script (EditorSceneManager) before
  quitting. Never leave the dialog blocking.
