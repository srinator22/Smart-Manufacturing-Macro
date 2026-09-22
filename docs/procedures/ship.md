# Ship - the done-gate

Run when a logical change is ready to land. Nothing ships around this
procedure; a push is complete only when every required check for the exact
SHA is terminal-success (kernel rule 5).

1. Confirm scope: `git status`. Unrelated or untracked work is preserved and
   excluded; resolve every untracked file deliberately (stage it or ignore
   it, never leave it ambiguous).
2. Run `./scripts/check.sh`. Anything red: stop and fix first.
3. Reviewer gate: an independent review verdict is present in
   `.work/TASK.md` (kernel rule 5). Quick-mode tasks may skip the reviewer
   only if the diff touches no logic (docs or config comments), and must say
   so explicitly.
4. Version: bump the single source-of-truth version file per semver (patch
   fix, minor feature, major breaking; pre-1.0 behavior per Project
   decisions).
5. Regenerate `CHANGELOG.md` via git-cliff. Never hand-edit it; the detail
   lives in commit bodies (kernel rule 18).
6. Conventional commit. The body carries why, and for fixes the exact
   issue, the fix, and the files and functions touched (kernel rule 6).
7. Push per the recorded workflow. Then run `scripts/ci-watch.sh` - delegate
   to the monitor agent when available. A push is complete only when every
   required check for the exact SHA is terminal-success. On failure:
   inspect logs, fix the root cause, verify locally, push the replacement,
   monitor the replacement SHA.
8. Release, when a coherent wave ships: releases are produced by
   `.github/workflows/release.yml`, not by hand. The human creates the
   annotated tag `vX.Y.Z` on the merged SHA and pushes it; the workflow
   refuses any tag whose version does not equal `Directory.Build.props`
   VersionPrefix, runs the full gate, packages every `projects/*/plugin.json`
   plugin, and calls `gh release create` with notes built from the
   CHANGELOG section plus a plugins-and-maturity table. Assets published:
   `WmpInventorTools-<version>.zip`, `SHA256SUMS.txt`, and
   `Install-WmpInventorTools.ps1`. Watch the release run to terminal state
   like any other push (step 7); a tag alone is not a release.
   Template-repo releases (before start runs) still use annotated
   `template-vX.Y.Z` tags and are cut by hand.
9. Final handoff format: Outcome / Evidence (commands and terminal results)
   / Artifacts (commit, release, deployment) / Limitations / Next action
   only if required.
