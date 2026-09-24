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
8. Release, when a coherent wave ships (ADR-0005 and its 2026-09-24
   amendment): CI cannot build working add-ins, because the Inventor
   interop exists only where Inventor 2027 is installed.
   a. Tag: the human creates the annotated tag `vX.Y.Z` on the merged SHA
      and pushes it.
   b. Workflow drafts: `.github/workflows/release.yml` refuses any tag
      whose version does not equal `Directory.Build.props` VersionPrefix,
      runs the full gate, and creates a DRAFT release with notes built from
      the CHANGELOG section plus a plugins-and-maturity table, and no
      assets. Watch the run to terminal state like any other push (step 7).
   c. Publish: on a machine with Inventor 2027, at the tag with a clean
      tree, run `pwsh -File scripts/release/publish-release.ps1 -Version
      X.Y.Z` (the workflow prints this command). It builds, runs the
      interop guard, uploads `WmpInventorTools-<version>.zip`,
      `SHA256SUMS.txt`, and `Install-WmpInventorTools.ps1`, and publishes
      the draft. `-DryRun` stops after the guard without touching GitHub.
   d. Watch: confirm `gh release view vX.Y.Z --json isDraft,assets` shows
      `isDraft: false` and all three assets. A tag or a draft alone is not
      a release.
   Template-repo releases (before start runs) still use annotated
   `template-vX.Y.Z` tags and are cut by hand.
9. Final handoff format: Outcome / Evidence (commands and terminal results)
   / Artifacts (commit, release, deployment) / Limitations / Next action
   only if required.
