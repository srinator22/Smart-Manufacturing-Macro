#!/usr/bin/env bash
# test-build-release.sh - Asserts build-release.ps1 rejects a catalog set with a duplicate id,
#   installDirectory, or derived manifest name, and rejects an invalid id, before any build or
#   staging happens; and that two clean, distinct catalogs clear validation.
#
# Purpose: Prove the catalog-uniqueness gate in build-release.ps1 fires for each of the three
#   collision fields plus the id format, names both offending catalog files (and, for the id
#   format check, the offending catalog), and never touches dist/ while rejecting. A fifth,
#   positive case proves two distinct catalogs pass validation by reaching the later
#   "Release output ... is missing" check instead of a validation error.
# Inputs: the working tree; run from anywhere, the script resolves the repository root itself.
# Outputs: "build-release-test: OK" on success; a non-zero exit and a named failure otherwise.
# Dependencies: bash, PowerShell 7 (pwsh), cygpath.
# Assumptions: build-release.ps1 accepts -ProjectsRoot and -DistRoot, so fixtures never sit under
#   projects/ and nothing is written under the repo's dist/.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

fail() { echo "BUILD-RELEASE TEST FAILED: $*" >&2; exit 1; }
need() { command -v "$1" >/dev/null 2>&1 || fail "$1 is required but was not found on PATH"; }

need pwsh
need cygpath

VERSION="$(sed -n 's:.*<VersionPrefix>\(.*\)</VersionPrefix>.*:\1:p' Directory.Build.props | head -n1)"
[[ -n "$VERSION" ]] || fail "could not read VersionPrefix from Directory.Build.props"

WORK_ROOT="$(mktemp -d -t wmp-build-release-XXXXXXXX)"

# Every case packages into its own dist root under the temp folder, so the repo's dist/ is
# never read or written by this test.
DIST_ROOT="$WORK_ROOT/dist"
DIST_STAGE="$DIST_ROOT/WmpInventorTools-$VERSION"

cleanup() {
  rm -rf "$WORK_ROOT"
}
trap cleanup EXIT

dist_stage_exists() { [[ -e "$DIST_STAGE" ]]; }

# create_project ROOT NAME ID INSTALL_DIR MANIFEST_BASE
# Writes a minimal projects/<name>/ fixture: an empty *.AddIn.csproj and an empty
# <manifest_base>.addin.template so build-release.ps1's existence checks pass, plus a
# plugin.json declaring every required field.
create_project() {
  local root="$1" name="$2" id="$3" install_dir="$4" manifest_base="$5"
  local dir="$root/$name"
  mkdir -p "$dir"
  : > "$dir/$name.AddIn.csproj"
  : > "$dir/$manifest_base.addin.template"
  # JSON has no \E escape, so a backslash in a fixture value (the path-escape cases below) has to be
  # doubled or ConvertFrom-Json rejects the fixture before build-release.ps1 can judge its contents.
  local install_dir_json="${install_dir//\\/\\\\}"
  local manifest_base_json="${manifest_base//\\/\\\\}"
  cat > "$dir/plugin.json" <<JSON
{
  "id": "$id",
  "displayName": "$name",
  "description": "$name fixture plugin",
  "maturity": "beta",
  "addinProject": "$name.AddIn.csproj",
  "assembly": "$name.AddIn.dll",
  "addinTemplate": "$manifest_base_json.addin.template",
  "installDirectory": "$install_dir_json",
  "ribbonPanel": "Test",
  "commands": ["Test.Command"],
  "homepage": "https://example.invalid/$name"
}
JSON
}

# run_case FIXTURE_PROJECTS_DIR - invokes the real script with -ProjectsRoot pointed at the
# fixture; sets CASE_OUTPUT and CASE_RC rather than letting `set -e` abort on the expected
# non-zero exit.
run_case() {
  local fixture_dir="$1"
  local fixture_w
  fixture_w="$(cygpath -w "$fixture_dir")"
  set +e
  CASE_OUTPUT="$(pwsh -NoProfile -File scripts/release/build-release.ps1 \
    -Version "$VERSION" -SkipBuild -ProjectsRoot "$fixture_w" -DistRoot "$(cygpath -w "$DIST_ROOT")" 2>&1)"
  CASE_RC=$?
  set -e
}

# --- duplicate id --------------------------------------------------------------
DUP_ID_ROOT="$WORK_ROOT/dup-id"
create_project "$DUP_ID_ROOT" "proj-a" "shared-id" "InstallA" "ManifestA"
create_project "$DUP_ID_ROOT" "proj-b" "shared-id" "InstallB" "ManifestB"
run_case "$DUP_ID_ROOT"
[[ "$CASE_RC" -ne 0 ]] || fail "duplicate id fixture exited 0; expected a rejection"
grep -q "shared-id" <<<"$CASE_OUTPUT" || fail "duplicate id message did not name the id: $CASE_OUTPUT"
grep -q "proj-a" <<<"$CASE_OUTPUT" || fail "duplicate id message did not name proj-a's catalog: $CASE_OUTPUT"
grep -q "proj-b" <<<"$CASE_OUTPUT" || fail "duplicate id message did not name proj-b's catalog: $CASE_OUTPUT"
dist_stage_exists && fail "the duplicate-id case wrote '$DIST_STAGE'" || true
echo "build-release-test: duplicate id rejected, no dist write OK"

# --- duplicate installDirectory -------------------------------------------------
DUP_DIR_ROOT="$WORK_ROOT/dup-installdir"
create_project "$DUP_DIR_ROOT" "proj-a" "proj-a-id" "SharedInstall" "ManifestA"
create_project "$DUP_DIR_ROOT" "proj-b" "proj-b-id" "SharedInstall" "ManifestB"
run_case "$DUP_DIR_ROOT"
[[ "$CASE_RC" -ne 0 ]] || fail "duplicate installDirectory fixture exited 0; expected a rejection"
grep -q "SharedInstall" <<<"$CASE_OUTPUT" || fail "duplicate installDirectory message did not name the value: $CASE_OUTPUT"
grep -q "proj-a" <<<"$CASE_OUTPUT" || fail "duplicate installDirectory message did not name proj-a's catalog: $CASE_OUTPUT"
grep -q "proj-b" <<<"$CASE_OUTPUT" || fail "duplicate installDirectory message did not name proj-b's catalog: $CASE_OUTPUT"
dist_stage_exists && fail "the duplicate-installDirectory case wrote '$DIST_STAGE'" || true
echo "build-release-test: duplicate installDirectory rejected, no dist write OK"

# --- duplicate manifest name -----------------------------------------------------
DUP_MANIFEST_ROOT="$WORK_ROOT/dup-manifest"
create_project "$DUP_MANIFEST_ROOT" "proj-a" "proj-a-id2" "InstallA2" "SharedManifest"
create_project "$DUP_MANIFEST_ROOT" "proj-b" "proj-b-id2" "InstallB2" "SharedManifest"
run_case "$DUP_MANIFEST_ROOT"
[[ "$CASE_RC" -ne 0 ]] || fail "duplicate manifest name fixture exited 0; expected a rejection"
grep -q "SharedManifest.addin" <<<"$CASE_OUTPUT" || fail "duplicate manifest name message did not name the manifest: $CASE_OUTPUT"
grep -q "proj-a" <<<"$CASE_OUTPUT" || fail "duplicate manifest name message did not name proj-a's catalog: $CASE_OUTPUT"
grep -q "proj-b" <<<"$CASE_OUTPUT" || fail "duplicate manifest name message did not name proj-b's catalog: $CASE_OUTPUT"
dist_stage_exists && fail "the duplicate-manifest-name case wrote '$DIST_STAGE'" || true
echo "build-release-test: duplicate manifest name rejected, no dist write OK"

# --- invalid id ------------------------------------------------------------------
BAD_ID_ROOT="$WORK_ROOT/bad-id"
create_project "$BAD_ID_ROOT" "proj-bad" "Bad Id" "InstallBad" "ManifestBad"
run_case "$BAD_ID_ROOT"
[[ "$CASE_RC" -ne 0 ]] || fail "invalid id fixture exited 0; expected a rejection"
grep -q "Bad Id" <<<"$CASE_OUTPUT" || fail "invalid id message did not name the id: $CASE_OUTPUT"
grep -q "proj-bad" <<<"$CASE_OUTPUT" || fail "invalid id message did not name its catalog: $CASE_OUTPUT"
dist_stage_exists && fail "the invalid-id case wrote '$DIST_STAGE'" || true
echo "build-release-test: invalid id rejected, no dist write OK"

# --- installDirectory that escapes the stage root ----------------------------------
# installDirectory is used verbatim as a path segment under the stage root and, at install time,
# under the Inventor Addins root. A value carrying a separator stages (and later installs) outside
# both roots, so it must be rejected before anything is written.
ESCAPE_DIR_ROOT="$WORK_ROOT/escape-installdir"
create_project "$ESCAPE_DIR_ROOT" "proj-escape" "proj-escape-id" '..\Escape' "ManifestEscape"
run_case "$ESCAPE_DIR_ROOT"
[[ "$CASE_RC" -ne 0 ]] || fail "an escaping installDirectory exited 0; expected a rejection"
grep -q "installDirectory" <<<"$CASE_OUTPUT" || fail "the escaping installDirectory message did not name the field: $CASE_OUTPUT"
grep -qF '..\Escape' <<<"$CASE_OUTPUT" || fail "the escaping installDirectory message did not name the value: $CASE_OUTPUT"
grep -q "proj-escape" <<<"$CASE_OUTPUT" || fail "the escaping installDirectory message did not name the plugin: $CASE_OUTPUT"
dist_stage_exists && fail "the escaping-installDirectory case wrote '$DIST_STAGE'" || true
[[ ! -e "$DIST_ROOT/Escape" ]] || fail "the escaping installDirectory staged outside the stage root at '$DIST_ROOT/Escape'"
echo "build-release-test: escaping installDirectory rejected, nothing staged outside the stage root OK"

# --- manifest name that is not a plain file name -----------------------------------
# The manifest name is derived from the template file name and is written into catalog.json, which
# the installer uses as a path segment under the Addins root. A dotted-prefix name is never a real
# manifest and is the shape a traversal takes, so the packager must not emit it.
ESCAPE_MANIFEST_ROOT="$WORK_ROOT/escape-manifest"
create_project "$ESCAPE_MANIFEST_ROOT" "proj-dotted" "proj-dotted-id" "InstallDotted" "..evil"
run_case "$ESCAPE_MANIFEST_ROOT"
[[ "$CASE_RC" -ne 0 ]] || fail "a dotted manifest template name exited 0; expected a rejection"
grep -q "manifest name" <<<"$CASE_OUTPUT" || fail "the dotted manifest name message did not name the field: $CASE_OUTPUT"
grep -qF '..evil.addin' <<<"$CASE_OUTPUT" || fail "the dotted manifest name message did not name the value: $CASE_OUTPUT"
grep -q "proj-dotted" <<<"$CASE_OUTPUT" || fail "the dotted manifest name message did not name the plugin: $CASE_OUTPUT"
dist_stage_exists && fail "the dotted-manifest-name case wrote '$DIST_STAGE'" || true
echo "build-release-test: dotted manifest name rejected, no dist write OK"

# --- valid, distinct catalogs pass validation -------------------------------------
# These fixtures have no Release build behind them, so this is expected to fail later, at the
# missing-assembly check; reaching that specific message (rather than a validation error) is
# the proof that a clean, distinct pair of catalogs clears the uniqueness and id-format gate.
VALID_ROOT="$WORK_ROOT/valid"
create_project "$VALID_ROOT" "proj-a" "proj-a-valid" "InstallAValid" "ManifestAValid"
create_project "$VALID_ROOT" "proj-b" "proj-b-valid" "InstallBValid" "ManifestBValid"
run_case "$VALID_ROOT"
[[ "$CASE_RC" -ne 0 ]] || fail "the valid fixture pair unexpectedly succeeded (no Release build exists for it)"
grep -q "Release output" <<<"$CASE_OUTPUT" && grep -q "is missing" <<<"$CASE_OUTPUT" \
  || fail "the valid fixture pair failed before the Release-output check, so validation was not proven: $CASE_OUTPUT"
echo "build-release-test: distinct catalogs pass validation OK"

echo "build-release-test: OK"
