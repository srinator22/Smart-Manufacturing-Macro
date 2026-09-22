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
