# ADR-0005: Release distribution and in-Inventor updates

Status: Accepted 2026-09-23 (human decision in session; three internal users, no code-signing certificate)

## Context

Installing an add-in today means cloning the repository, having the .NET SDK, building, and running a script. The intended audience is three WMP engineers who use Inventor, not developers. The user asked for a one-step install and an Update command on the ribbon that fetches new versions and new plugins from GitHub. There is no Authenticode certificate and there will not be one at this scale. Add-in DLLs are locked by Inventor while it runs, so no in-process updater can replace them live.

## Decision

1. **Binaries are distributed only through GitHub Releases.** A `v*` tag triggers a release workflow that builds Release, packages every add-in under `projects/*/` with its `.addin` manifest into `WmpInventorTools-<version>.zip`, publishes `SHA256SUMS.txt`, and attaches `Install-WmpInventorTools.ps1`. The zip is the unit of install and update for all plugins together; per-plugin release trains still require their own ADR (ADR-0004).
2. **No executable ships.** Install and update are PowerShell only. The installer runs in memory (`irm <release url> | iex`), downloads the zip, verifies SHA-256, removes the Mark-of-the-Web with `Unblock-File`, extracts to `%APPDATA%\Autodesk\Inventor 2027\Addins\`, and writes manifests. SmartScreen warns about downloaded executables the user launches; this flow launches none, so no warning appears. Integrity comes from HTTPS to a pinned repository plus the published hash, not from a signature. This is accepted for three internal users and must be revisited before any wider distribution.
3. **A third add-in, WMP Tools Manager, owns the Update command** on the shared `WMP Custom Tools` tab. It queries the public Releases API without a token, compares the latest tag with the installed version, shows the changelog excerpt, and on the user's click downloads and verifies the zip into `%LOCALAPPDATA%\WMP\InventorTools\staging\`. It then launches the system `powershell.exe` with a staged apply script that waits for every Inventor process to exit, keeps the current install as `previous\` for one-step rollback, copies staging into place, and writes manifests for any plugin in the release that is not yet installed. The add-in tells the user to close Inventor to finish. Updates are never silent: check-and-prompt only; an optional startup check is off by default.
4. **Version source stays `Directory.Build.props`.** The installer and updater compare the release tag with the version recorded in the installed manifests; the workspace releases as one.

## Consequences

- The ship procedure's release step becomes real: a tag now produces artifacts. `0.4.0` and `0.5.0` were never tagged; the first published release is the one that ships this decision.
- The .NET 10 Desktop runtime must exist on the target machine; the installer detects it and points to Microsoft's installer rather than bundling it.
- A user who bypasses the installer, downloads the zip in a browser, and double-clicks a file may see SmartScreen or a blocked DLL; the README documents only the supported path.
- Rollback is a folder swap; the updater never deletes a previous install, it archives it.
- The GitHub API is rate-limited for unauthenticated calls (60 per hour per IP); the check is user-initiated, so this is not a practical limit.

## Alternatives rejected

- Bundling an updater executable: introduces the one artifact SmartScreen would flag and gains nothing PowerShell cannot do.
- Silent auto-update on Inventor start: violates the workspace's rule that outward-facing or irreversible actions need a human decision, and engineering tooling must not change under a user mid-project.
- Self-signed certificate: Windows treats it as untrusted unless installed on every machine, which is more work than the problem.

## Amendment 2026-09-24: add-in assets are built on a machine with Inventor 2027

Status: Accepted 2026-09-24 (P0 defect: v0.6.0 shipped add-ins that do not load).

### Why CI cannot build the add-ins

Every add-in project references `Autodesk.Inventor.Interop.dll` from the Inventor 2027 install and defines `INVENTOR_INTEROP` only when that file exists. The interop assembly is Autodesk's and ships only with Inventor, so a GitHub-hosted runner does not have it and it may not be committed (Project decisions data policy). Without it the build still succeeds: every `#if INVENTOR_INTEROP` block, including `StandardAddInServer`, compiles out. The v0.6.0 release workflow did exactly that; its three add-in DLLs (6.6 to 9.7 KB) load in Inventor as Unloaded with no ribbon command, and the packaging step only checked that each DLL existed.

### What changes

1. **Decision 1 is amended: the release workflow no longer builds or attaches binaries.** A `v*` tag push runs `.github/workflows/release.yml`, which verifies the tag equals `Directory.Build.props` VersionPrefix, runs the full gate, composes the notes from the CHANGELOG section plus the plugins-and-maturity table read from `projects/*/plugin.json`, and creates the GitHub Release as a **draft** with those notes and no assets. Its last step prints the publish command. A draft is not returned by the Releases `latest` API, so neither the installer one-liner nor the Tools Manager updater can see a release before its assets exist.
2. **Publishing is a developer-machine step.** On a machine with Inventor 2027, at the tag with a clean tree, the developer runs `pwsh -File scripts/release/publish-release.ps1 -Version <version>`. It refuses a HEAD that is not the tag or a dirty tree, requires the interop at the default `InventorInteropPath`, runs `build-release.ps1`, uploads `WmpInventorTools-<version>.zip`, `SHA256SUMS.txt` and `Install-WmpInventorTools.ps1` to the draft, publishes it, and prints the asset list GitHub reports. `-DryRun` (or `-WhatIf`) stops after the guard and prints what would be uploaded.
3. **The package is guarded.** `build-release.ps1` reads each add-in assembly's metadata before anything under `dist/` is written and refuses the package, naming each DLL, unless it references `Autodesk.Inventor.Interop` and defines a type named `StandardAddInServer`. `scripts/release/test-build-release.sh` proves the refusal against a real add-in compiled with a missing `InventorInteropPath`; `scripts/release/test-release.sh` proves the real Release output passes on a machine with Inventor and is refused on one without, where it then exercises the installer over a stub add-in that carries both metadata facts.

### Consequences

- A tag alone is still not a release, and now neither is the workflow run: a release is done only when `publish-release.ps1` has reported the three assets on a non-draft release.
- The release binaries are built on a developer machine, not in CI. Reproducibility rests on the tag check, the clean-tree check, the pinned SDK, and the guard; this is accepted for three internal users.
- The `v0.6.0` tag predates `publish-release.ps1` and the guard, so its published assets are not repaired by this change; replacing them in place or superseding them with the next release is a separate decision.

### Publish refusals (review follow-up, 2026-09-24)

An independent review found that `-AllowUntagged` turned the tag check into a warning on a real run, that `-SkipBuild` let a real run package gitignored `bin/Release` output the clean-tree check cannot see, and that an already-published release was accepted with a warning and then overwritten with `--clobber`. The rules are now:

1. **`-AllowUntagged` and `-SkipBuild` are dry-run only.** A real run with either stops with an error before it reads the release, builds or uploads. A real publish is always rebuilt from the commit the tag names with a clean tree.
2. **A published release is refused by default.** A real run reads the release state before it builds; a release that is no longer a draft is refused unless `-ReplacePublishedAssets` is given. That switch is the documented path for repairing a broken release in place. It still requires HEAD at the tag and a clean tree, cannot be combined with `-AllowUntagged`, and prints the asset names it will overwrite and the digests in the currently published `SHA256SUMS.txt` before it builds, then the new digests before it uploads. It does not re-run `--draft=false` on a release that is already published.
3. **The refusals are tested.** `scripts/release/test-publish-release.sh` runs against a throwaway clone tagged `v9.9.9`, with `gh` shadowed by a logging shim, and asserts each refusal happens before any build or GitHub write and that a dry run makes no `gh` call.
