#!/usr/bin/env bash
# test-publish-release.sh - Asserts publish-release.ps1 refuses every unsafe publish before it builds
#   or reaches GitHub, and that a dry run never calls gh at all.
#
# Purpose: publish-release.ps1 is the only step that attaches binaries to a GitHub Release
#   (ADR-0005, 2026-09-24 amendment). Its refusals are the release's last line of defence, so each one
#   is exercised here against a throwaway clone:
#     a. a dirty working tree is refused;
#     b. a HEAD that is not the tag's commit is refused;
#     c. -AllowUntagged on a real run is refused (it is dry-run only);
#     d. -SkipBuild on a real run is refused (it is dry-run only);
#     e. a tag that does not exist is refused;
#     f. -DryRun -SkipBuild on the clean tagged clone runs the interop checks and lists the would-be
#        uploads, calling gh zero times;
#     g. -ReplacePublishedAssets from a HEAD that is not the tag is refused;
#     h. a release that is already published (not a draft) is refused without -ReplacePublishedAssets;
#     i. -ReplacePublishedAssets prints the asset names it will overwrite and the current published
#        SHA256SUMS.txt digests before it goes any further;
#     j. a real (non-dry) publish against a clean, correctly tagged clone is refused when `gh release
#        view` itself fails (e.g. the draft release does not exist yet), before the interop check and
#        the build ever run;
#     k. a real publish's success path uploads the three assets, undrafts the release, and reports the
#        asset list and non-draft state read back from the post-upload `gh release view` (skipped, with
#        a printed reason, when the Inventor interop DLL is not installed locally, since CI has none).
# Inputs: the working tree; run from anywhere, the script resolves the repository root itself. The
#   working-tree publish-release.ps1, build-release.ps1 and Install-WmpInventorTools.ps1 are copied
#   into the clone and committed there, so the test exercises what is on disk, not only what is
#   committed.
# Outputs: "publish-release-test: OK" on success; a non-zero exit and a named failure otherwise.
# Dependencies: bash, git, PowerShell 7 (pwsh), cygpath, dotnet SDK (only for case f's fallback build
#   and case k's success-path restore/build, both skipped when the Inventor interop DLL is absent).
# Assumptions: nothing here may reach GitHub. gh is shadowed by a shim directory placed first on PATH;
#   the shim logs every invocation and fails unless a case explicitly scripts a read-only answer (h and
#   i answer `release view` and `release download` from local files; k answers `release view`, `release
#   upload` and `release edit` while GH_SHIM_MODE=accept). As a second barrier GH_HOST, GH_TOKEN and
#   GH_CONFIG_DIR point nowhere usable and -Repo names a repository that does not exist, so even an
#   unshadowed gh could not touch the real release. The clone is tagged v9.9.9 with a matching
#   VersionPrefix, so no real tag or version is involved.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

fail() { echo "PUBLISH-RELEASE TEST FAILED: $*" >&2; exit 1; }
need() { command -v "$1" >/dev/null 2>&1 || fail "$1 is required but was not found on PATH"; }

need git
need pwsh
need cygpath

TEST_VERSION="9.9.9"
TEST_TAG="v$TEST_VERSION"
TEST_REPO="publish-release-test/does-not-exist"

WORK_ROOT="$(mktemp -d -t wmp-publish-release-XXXXXXXX)"
cleanup() { rm -rf "$WORK_ROOT"; }
trap cleanup EXIT

CLONE="$WORK_ROOT/clone"
DIST_W="$(cygpath -w "$WORK_ROOT/dist")"

# Commits and tags exist only inside the throwaway clone; the identity and signing settings are
# pinned so the test does not depend on the caller's git configuration.
clone_git() {
  git -C "$CLONE" -c user.name=publish-release-test -c user.email=publish-release-test@example.invalid \
    -c commit.gpgsign=false -c tag.gpgsign=false -c core.safecrlf=false "$@"
}

# --- throwaway clone at a tag of its own ------------------------------------------------------
git clone --quiet "$ROOT" "$CLONE"
[[ "$(git -C "$CLONE" rev-parse HEAD)" == "$(git -C "$ROOT" rev-parse HEAD)" ]] \
  || fail "the clone is not at the working tree's HEAD"
for script in publish-release.ps1 build-release.ps1 Install-WmpInventorTools.ps1; do
  cp "$ROOT/scripts/release/$script" "$CLONE/scripts/release/$script"
done
sed -i "s:<VersionPrefix>[^<]*</VersionPrefix>:<VersionPrefix>$TEST_VERSION</VersionPrefix>:" \
  "$CLONE/Directory.Build.props"
grep -q "<VersionPrefix>$TEST_VERSION</VersionPrefix>" "$CLONE/Directory.Build.props" \
  || fail "could not set VersionPrefix $TEST_VERSION in the clone"
clone_git commit --quiet -am "test: release scripts from the working tree at $TEST_VERSION"
clone_git tag -a "$TEST_TAG" -m "publish-release test tag"
[[ -z "$(git -C "$CLONE" status --porcelain)" ]] || fail "the tagged clone is not clean"

# --- gh shim ---------------------------------------------------------------------------------
SHIM_DIR="$WORK_ROOT/gh-shim"
GH_LOG="$WORK_ROOT/gh-invocations.log"
mkdir -p "$SHIM_DIR" "$WORK_ROOT/gh-config"
cat > "$SHIM_DIR/gh-shim.ps1" <<'PS_EOF'
# Logs every gh invocation, unconditionally, before any mode branch below - the log is the record a
# test case asserts the exact call sequence against.
#
# GH_SHIM_MODE=published answers only the two read-only calls the already-published path makes.
# GH_SHIM_MODE=accept answers the success path: the first `release view` (fields
# tagName,isDraft,assets) reports a draft; `release upload` and `release edit --draft=false` succeed;
# the second `release view` (fields isDraft,assets, made after upload) reports the release published
# with its assets. Any other mode (e.g. "fail") answers nothing, so every call - including the very
# first `release view` - fails, which is what proves a missing draft release is refused before the
# interop check and the build (case j).
Add-Content -LiteralPath $env:GH_SHIM_LOG -Value ("gh " + ($args -join ' '))
if ($args.Count -ge 2 -and $args[0] -eq 'release') {
    if ($env:GH_SHIM_MODE -eq 'published') {
        if ($args[1] -eq 'view') {
            Get-Content -Raw -LiteralPath $env:GH_SHIM_VIEW_JSON
            exit 0
        }
        if ($args[1] -eq 'download') {
            $outputIndex = [array]::IndexOf($args, '--output')
            if ($outputIndex -ge 0 -and $outputIndex + 1 -lt $args.Count) {
                Copy-Item -LiteralPath $env:GH_SHIM_SUMS -Destination $args[$outputIndex + 1] -Force
                exit 0
            }
        }
    }
    if ($env:GH_SHIM_MODE -eq 'accept') {
        if ($args[1] -eq 'view') {
            $jsonIndex = [array]::IndexOf($args, '--json')
            $jsonFields = if ($jsonIndex -ge 0 -and $jsonIndex + 1 -lt $args.Count) { $args[$jsonIndex + 1] } else { '' }
            if ($jsonFields -eq 'isDraft,assets') {
                Get-Content -Raw -LiteralPath $env:GH_SHIM_ACCEPT_PUBLISHED_JSON
            }
            else {
                Get-Content -Raw -LiteralPath $env:GH_SHIM_ACCEPT_DRAFT_JSON
            }
            exit 0
        }
        if ($args[1] -eq 'upload' -or $args[1] -eq 'edit') {
            exit 0
        }
    }
}
[Console]::Error.WriteLine("gh shim: refusing '$($args -join ' ')'")
exit 97
PS_EOF
printf '@pwsh -NoProfile -NonInteractive -File "%s" %%*\r\n@exit /b %%ERRORLEVEL%%\r\n' \
  "$(cygpath -w "$SHIM_DIR/gh-shim.ps1")" > "$SHIM_DIR/gh.cmd"

VIEW_JSON="$WORK_ROOT/gh-view.json"
PUBLISHED_SUMS="$WORK_ROOT/published-SHA256SUMS.txt"
FAKE_ZIP_DIGEST="1111111111111111111111111111111111111111111111111111111111111111"
FAKE_INSTALLER_DIGEST="2222222222222222222222222222222222222222222222222222222222222222"
cat > "$VIEW_JSON" <<JSON
{"tagName":"$TEST_TAG","isDraft":false,"assets":[
  {"name":"WmpInventorTools-$TEST_VERSION.zip","size":123},
  {"name":"SHA256SUMS.txt","size":45},
  {"name":"Install-WmpInventorTools.ps1","size":67}]}
JSON
printf '%s  WmpInventorTools-%s.zip\n%s  Install-WmpInventorTools.ps1\n' \
  "$FAKE_ZIP_DIGEST" "$TEST_VERSION" "$FAKE_INSTALLER_DIGEST" > "$PUBLISHED_SUMS"

# Fixtures for GH_SHIM_MODE=accept (case k, the success path): a draft before upload, and the same
# release published with the three real asset names afterwards.
ACCEPT_DRAFT_JSON="$WORK_ROOT/gh-view-accept-draft.json"
ACCEPT_PUBLISHED_JSON="$WORK_ROOT/gh-view-accept-published.json"
cat > "$ACCEPT_DRAFT_JSON" <<JSON
{"tagName":"$TEST_TAG","isDraft":true,"assets":[]}
JSON
cat > "$ACCEPT_PUBLISHED_JSON" <<JSON
{"isDraft":false,"assets":[
  {"name":"WmpInventorTools-$TEST_VERSION.zip","size":123},
  {"name":"SHA256SUMS.txt","size":45},
  {"name":"Install-WmpInventorTools.ps1","size":67}]}
JSON

# run_publish MODE ARGS... - runs the clone's publish-release.ps1 with the shim first on PATH and sets
# CASE_OUTPUT and CASE_RC rather than letting `set -e` abort on the expected non-zero exit. MODE is
# "fail" (every gh call fails), "published" or "accept" (see the shim). -DistRoot is $DIST_W unless
# RUN_PUBLISH_DIST_ROOT is set for the call, which lets a case prove build-release wrote nothing by
# pointing at a directory of its own that must stay absent.
run_publish() {
  local mode="$1"
  shift
  local distRoot="${RUN_PUBLISH_DIST_ROOT:-$DIST_W}"
  rm -f "$GH_LOG"
  set +e
  CASE_OUTPUT="$(cd "$CLONE" && PATH="$SHIM_DIR:$PATH" \
    GH_SHIM_MODE="$mode" \
    GH_SHIM_LOG="$(cygpath -w "$GH_LOG")" \
    GH_SHIM_VIEW_JSON="$(cygpath -w "$VIEW_JSON")" \
    GH_SHIM_SUMS="$(cygpath -w "$PUBLISHED_SUMS")" \
    GH_SHIM_ACCEPT_DRAFT_JSON="$(cygpath -w "$ACCEPT_DRAFT_JSON")" \
    GH_SHIM_ACCEPT_PUBLISHED_JSON="$(cygpath -w "$ACCEPT_PUBLISHED_JSON")" \
    GH_HOST="gh-shim.invalid" GH_TOKEN="publish-release-test-invalid" \
    GH_CONFIG_DIR="$(cygpath -w "$WORK_ROOT/gh-config")" \
    pwsh -NoProfile -File "$(cygpath -w "$CLONE/scripts/release/publish-release.ps1")" \
      -Repo "$TEST_REPO" -DistRoot "$distRoot" "$@" 2>&1)"
  CASE_RC=$?
  set -e
}

assert_refused() {
  local label="$1"
  [[ "$CASE_RC" -ne 0 ]] || fail "$label exited 0; expected a refusal: $CASE_OUTPUT"
}
assert_output() {
  local label="$1" needle="$2"
  grep -qF -- "$needle" <<<"$CASE_OUTPUT" || fail "$label did not print '$needle': $CASE_OUTPUT"
}
assert_no_gh() {
  local label="$1"
  [[ ! -e "$GH_LOG" ]] || fail "$label invoked gh: $(cat "$GH_LOG")"
}
assert_not_built() {
  local label="$1"
  if grep -qF "build-release:" <<<"$CASE_OUTPUT"; then
    fail "$label reached build-release.ps1 before refusing: $CASE_OUTPUT"
  fi
}
assert_clone_clean() {
  [[ -z "$(git -C "$CLONE" status --porcelain)" ]] \
    || fail "$1 left the clone dirty: $(git -C "$CLONE" status --porcelain)"
}

# The same default InventorInteropPath publish-release.ps1 requires; case f's expectation depends on it.
INTEROP_PRESENT="$(pwsh -NoProfile -Command \
  'Test-Path -LiteralPath (Join-Path $env:ProgramFiles "Autodesk\Inventor 2027\Bin\Public Assemblies\Autodesk.Inventor.Interop.dll") -PathType Leaf' \
  | tr -d '\r')"

# bin/ is gitignored, so Release output copied into the clone leaves it clean - which is exactly why a
# real -SkipBuild publish is refused (case d). Only case f's dry run is meant to package it.
if [[ "$INTEROP_PRESENT" == "True" ]]; then
  addin_projects=(projects/*/src/*.AddIn/*.AddIn.csproj)
  [[ -e "${addin_projects[0]}" ]] || fail "no add-in projects found under projects/*/src/*.AddIn/"
  missing_output=0
  for csproj in "${addin_projects[@]}"; do
    [[ -f "$(dirname "$csproj")/bin/Release/net10.0-windows/$(basename "$csproj" .csproj).dll" ]] || missing_output=1
  done
  if (( missing_output )); then
    echo "publish-release-test: Release output is missing; building the solution in Release for case f"
    dotnet build InventorScripts.sln -c Release --no-restore >/dev/null \
      || fail "could not build the solution in Release for case f"
  fi
  for csproj in "${addin_projects[@]}"; do
    release_dir="$(dirname "$csproj")/bin/Release/net10.0-windows"
    mkdir -p "$CLONE/$release_dir"
    cp -R "$release_dir/." "$CLONE/$release_dir/"
  done
  assert_clone_clean "copying Release output"
fi

# --- a. dirty working tree -------------------------------------------------------------------
cp "$CLONE/README.md" "$WORK_ROOT/README.md.bak"
echo "publish-release-test: uncommitted edit" >> "$CLONE/README.md"
run_publish fail -Version "$TEST_VERSION"
assert_refused "a dirty tree"
assert_output "a dirty tree" "uncommitted or untracked changes"
assert_no_gh "a dirty tree"
assert_not_built "a dirty tree"
cp "$WORK_ROOT/README.md.bak" "$CLONE/README.md"
assert_clone_clean "restoring README.md"
echo "publish-release-test: a. dirty tree refused before build, no gh OK"

# --- d. -SkipBuild on a real run ----------------------------------------------------------
run_publish fail -Version "$TEST_VERSION" -SkipBuild
assert_refused "-SkipBuild without -DryRun"
assert_output "-SkipBuild without -DryRun" "-SkipBuild is accepted only with -DryRun or -WhatIf"
assert_no_gh "-SkipBuild without -DryRun"
assert_not_built "-SkipBuild without -DryRun"
echo "publish-release-test: d. -SkipBuild real run refused before build, no gh OK"

# --- e. missing tag ------------------------------------------------------------------------
run_publish fail -Version "9.9.8"
assert_refused "a missing tag"
assert_output "a missing tag" "the tag v9.9.8 does not exist locally"
assert_no_gh "a missing tag"
assert_not_built "a missing tag"
echo "publish-release-test: e. missing tag refused before build, no gh OK"

# --- f. dry run on the clean tagged clone ----------------------------------------------------
run_publish fail -Version "$TEST_VERSION" -DryRun -SkipBuild
assert_no_gh "the dry run"
assert_output "the dry run" "DRY RUN"
if [[ "$INTEROP_PRESENT" == "True" ]]; then
  [[ "$CASE_RC" -eq 0 ]] || fail "the dry run on the clean tagged clone exited $CASE_RC: $CASE_OUTPUT"
  assert_output "the dry run" "Inventor interop found at"
  assert_output "the dry run" "build-release: interop guard OK for"
  assert_output "the dry run" "Would upload to $TEST_REPO release $TEST_TAG"
  assert_output "the dry run" "WmpInventorTools-$TEST_VERSION.zip"
  assert_output "the dry run" "SHA256SUMS.txt"
  assert_output "the dry run" "Install-WmpInventorTools.ps1"
  echo "publish-release-test: f. dry run passed the interop guard and listed the uploads, no gh OK"
else
  assert_refused "the dry run without the Inventor interop"
  assert_output "the dry run without the Inventor interop" "Autodesk.Inventor.Interop.dll is not at"
  echo "publish-release-test: f. Inventor 2027 is not installed here, so the dry run stops at the interop presence check instead of listing uploads; asserted that refusal, no gh OK"
fi

# --- h. already-published release without -ReplacePublishedAssets ------------------------------
run_publish published -Version "$TEST_VERSION"
assert_refused "an already-published release"
assert_output "an already-published release" "is already published"
assert_output "an already-published release" "-ReplacePublishedAssets"
assert_not_built "an already-published release"
[[ -e "$GH_LOG" ]] || fail "the already-published case never asked gh for the release state"
if grep -qv "^gh release view " "$GH_LOG"; then
  fail "the already-published case made a gh call other than release view: $(cat "$GH_LOG")"
fi
echo "publish-release-test: h. already-published release refused before build, only release view OK"

# --- i. -ReplacePublishedAssets lists what it will overwrite -----------------------------------
# The shim answers only the read-only calls, so whatever stops the run afterwards (the interop presence
# check, the build, or the shim refusing the upload), nothing is ever uploaded; the assertion is that
# the overwrite listing and the current published digests are printed first.
run_publish published -Version "$TEST_VERSION" -ReplacePublishedAssets
assert_output "-ReplacePublishedAssets" "will be overwritten"
assert_output "-ReplacePublishedAssets" "WmpInventorTools-$TEST_VERSION.zip (123 bytes)"
assert_output "-ReplacePublishedAssets" "Install-WmpInventorTools.ps1 (67 bytes)"
assert_output "-ReplacePublishedAssets" "$FAKE_ZIP_DIGEST  WmpInventorTools-$TEST_VERSION.zip"
assert_output "-ReplacePublishedAssets" "$FAKE_INSTALLER_DIGEST  Install-WmpInventorTools.ps1"
if grep -Ev "^gh release (view|download|upload) " "$GH_LOG" >/dev/null; then
  fail "-ReplacePublishedAssets made an unexpected gh call: $(cat "$GH_LOG")"
fi
assert_refused "-ReplacePublishedAssets against the shim"
assert_clone_clean "the -ReplacePublishedAssets case"
echo "publish-release-test: i. -ReplacePublishedAssets printed the overwrite list and published digests first OK"

# --- j. draft release missing: `gh release view` itself fails on a real run (F1) ---------------
# Every other refusal case above never reaches the gh call (assert_no_gh). This is the one refusal
# that happens after Invoke-Gh is actually called: the clone is clean and at the tag, so nothing is
# refused locally, and the shim (mode "fail") answers no call at all - including this first
# `release view` - so it fails exactly as it would for a tag with no draft release yet.
CASE_J_DIST="$WORK_ROOT/dist-case-j"
RUN_PUBLISH_DIST_ROOT="$(cygpath -w "$CASE_J_DIST")" run_publish fail -Version "$TEST_VERSION"
unset RUN_PUBLISH_DIST_ROOT
assert_refused "a missing draft release"
assert_output "a missing draft release" "$TEST_TAG"
assert_output "a missing draft release" "gh release view $TEST_TAG"
assert_not_built "a missing draft release"
[[ -e "$GH_LOG" ]] || fail "a missing draft release never called gh"
[[ "$(wc -l < "$GH_LOG")" -eq 1 ]] \
  || fail "a missing draft release's gh log has more than the one release-view call: $(cat "$GH_LOG")"
grep -qF -- "gh release view $TEST_TAG" "$GH_LOG" \
  || fail "a missing draft release's only gh call was not release view: $(cat "$GH_LOG")"
[[ ! -e "$CASE_J_DIST" ]] \
  || fail "a missing draft release produced dist output at $CASE_J_DIST even though build-release must never run"
assert_clone_clean "a missing draft release"
echo "publish-release-test: j. draft release missing (gh release view failed) refused before the interop check and the build, only release view OK"

# --- k. success path: upload, undraft, and the post-upload checks (F2) -------------------------
# The only case here that lets a real, non-dry run reach GitHub. It needs a real Release build (a real
# run refuses -SkipBuild, case d), which needs a restore the clone does not carry (git ignores
# obj/bin); that restore and the one build it enables are done once, here, only when this machine has
# the Inventor interop DLL - CI does not, so the case prints why it is skipped instead of failing.
if [[ "$INTEROP_PRESENT" == "True" ]]; then
  echo "publish-release-test: k. restoring the clone's solution once for the success-path build"
  (cd "$CLONE" && dotnet restore InventorScripts.sln --locked-mode >/dev/null) \
    || fail "could not restore the clone's solution for case k"

  CASE_K_DIST="$WORK_ROOT/dist-case-k"
  RUN_PUBLISH_DIST_ROOT="$(cygpath -w "$CASE_K_DIST")" run_publish accept -Version "$TEST_VERSION"
  unset RUN_PUBLISH_DIST_ROOT
  [[ "$CASE_RC" -eq 0 ]] || fail "the success path exited $CASE_RC: $CASE_OUTPUT"
  assert_output "the success path" "Inventor interop found at"
  assert_output "the success path" "publish-release: uploaded 3 assets to $TEST_TAG"
  assert_output "the success path" "publish-release: $TEST_TAG is published"
  assert_output "the success path" "publish-release: assets on $TEST_TAG"
  assert_output "the success path" "WmpInventorTools-$TEST_VERSION.zip"
  assert_output "the success path" "SHA256SUMS.txt"
  assert_output "the success path" "Install-WmpInventorTools.ps1"

  [[ -e "$GH_LOG" ]] || fail "the success path never called gh"
  # pwsh's Add-Content writes CRLF; strip the \r so exact-string comparisons below aren't defeated by
  # an invisible trailing carriage return.
  mapfile -t GH_CALLS < <(tr -d '\r' < "$GH_LOG")
  [[ "${#GH_CALLS[@]}" -eq 4 ]] \
    || fail "the success path made ${#GH_CALLS[@]} gh calls, expected 4 (view, upload, edit, view): $(cat "$GH_LOG")"
  [[ "${GH_CALLS[0]}" == "gh release view $TEST_TAG --repo $TEST_REPO --json tagName,isDraft,assets" ]] \
    || fail "the success path's 1st gh call was not the draft release view: ${GH_CALLS[0]}"
  [[ "${GH_CALLS[1]}" == "gh release upload $TEST_TAG "* ]] \
    || fail "the success path's 2nd gh call was not release upload: ${GH_CALLS[1]}"
  for name in "WmpInventorTools-$TEST_VERSION.zip" "SHA256SUMS.txt" "Install-WmpInventorTools.ps1"; do
    grep -qF -- "$name" <<<"${GH_CALLS[1]}" \
      || fail "the success path's release upload did not include $name: ${GH_CALLS[1]}"
  done
  grep -qF -- "--clobber" <<<"${GH_CALLS[1]}" \
    || fail "the success path's release upload did not pass --clobber: ${GH_CALLS[1]}"
  [[ "${GH_CALLS[2]}" == "gh release edit $TEST_TAG --repo $TEST_REPO --draft=false" ]] \
    || fail "the success path's 3rd gh call was not release edit --draft=false: ${GH_CALLS[2]}"
  [[ "${GH_CALLS[3]}" == "gh release view $TEST_TAG --repo $TEST_REPO --json isDraft,assets" ]] \
    || fail "the success path's 4th gh call was not the post-upload release view: ${GH_CALLS[3]}"

  assert_clone_clean "the success path"
  echo "publish-release-test: k. success path uploaded the 3 assets, undrafted $TEST_TAG and reported the asset list and not-draft OK"
else
  echo "publish-release-test: k. Inventor 2027 is not installed here, so the success path cannot build; skipped (CI has no interop either)"
fi

# --- move HEAD past the tag --------------------------------------------------------------------
clone_git commit --quiet --allow-empty -m "test: move HEAD past the tag"
assert_clone_clean "the empty commit"

# --- b. HEAD not at the tag ------------------------------------------------------------------
run_publish fail -Version "$TEST_VERSION"
assert_refused "a HEAD past the tag"
assert_output "a HEAD past the tag" "is not the commit"
assert_no_gh "a HEAD past the tag"
assert_not_built "a HEAD past the tag"
echo "publish-release-test: b. HEAD not at the tag refused before build, no gh OK"

# --- c. -AllowUntagged on a real run --------------------------------------------------------
run_publish fail -Version "$TEST_VERSION" -AllowUntagged
assert_refused "-AllowUntagged without -DryRun"
assert_output "-AllowUntagged without -DryRun" "-AllowUntagged is accepted only with -DryRun or -WhatIf"
assert_no_gh "-AllowUntagged without -DryRun"
assert_not_built "-AllowUntagged without -DryRun"
echo "publish-release-test: c. -AllowUntagged real run refused before build, no gh OK"

# --- g. -ReplacePublishedAssets from a HEAD not at the tag --------------------------------------
run_publish published -Version "$TEST_VERSION" -ReplacePublishedAssets
assert_refused "-ReplacePublishedAssets from a HEAD past the tag"
assert_output "-ReplacePublishedAssets from a HEAD past the tag" "is not the commit"
assert_no_gh "-ReplacePublishedAssets from a HEAD past the tag"
assert_not_built "-ReplacePublishedAssets from a HEAD past the tag"
echo "publish-release-test: g. -ReplacePublishedAssets from a HEAD past the tag refused before build, no gh OK"

echo "publish-release-test: OK"
