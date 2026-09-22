#!/usr/bin/env bash
# test-release.sh - Asserts the release package and the installer behave as ADR-0005 requires.
#
# Purpose: Prove, without Inventor and without a GitHub release, that build-release.ps1 produces a
#   complete verifiable package and that Install-WmpInventorTools.ps1 installs, rejects a tampered
#   digest, is idempotent, and rolls back.
# Inputs: the working tree; run from anywhere, the script resolves the repository root itself.
# Outputs: "release-package: OK" on success; a non-zero exit and a named failure otherwise.
# Dependencies: bash, PowerShell 7 (pwsh), the .NET SDK, sha256sum, unzip, cygpath.
# Assumptions: Inventor is not running; the installer refuses to touch add-in files while it is.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

fail() { echo "RELEASE TEST FAILED: $*" >&2; exit 1; }
need() { command -v "$1" >/dev/null 2>&1 || fail "$1 is required but was not found on PATH"; }

need pwsh
need dotnet
need sha256sum
need unzip
need cygpath

VERSION="$(sed -n 's:.*<VersionPrefix>\(.*\)</VersionPrefix>.*:\1:p' Directory.Build.props | head -n1)"
[[ -n "$VERSION" ]] || fail "could not read VersionPrefix from Directory.Build.props"

INSTALLER="$(cygpath -w "$ROOT/scripts/release/Install-WmpInventorTools.ps1")"
TEST_ROOT="$(mktemp -d -t wmp-release-XXXXXXXX)"
cleanup() { rm -rf "$TEST_ROOT"; }
trap cleanup EXIT

echo "release-test: building Release once"
dotnet restore InventorScripts.sln --locked-mode >/dev/null
dotnet build InventorScripts.sln -c Release --no-restore >/dev/null

echo "release-test: packaging $VERSION"
pwsh -NoProfile -File scripts/release/build-release.ps1 -Version "$VERSION" -SkipBuild >/dev/null

ZIP="dist/WmpInventorTools-$VERSION.zip"
SUMS="dist/SHA256SUMS.txt"
[[ -f "$ZIP" ]] || fail "$ZIP was not produced"
[[ -f "$SUMS" ]] || fail "$SUMS was not produced"

# --- package contents --------------------------------------------------------
ENTRIES="$(unzip -Z1 "$ZIP")"
for required in \
  "catalog.json" \
  "Install-WmpInventorTools.ps1" \
  "FileNamingManager/FileNamingManager.AddIn.dll" \
  "SmartManufacturingExporter/SmartManufacturingExporter.AddIn.dll" \
  "templates/file-naming-manager.addin.template" \
  "templates/smart-manufacturing-exporter.addin.template"
do
  grep -Fxq "$required" <<<"$ENTRIES" || fail "the package does not contain $required"
done
echo "release-test: package contents OK"

# --- digests -----------------------------------------------------------------
( cd dist && sha256sum --check --quiet <(sed "s#  Install-WmpInventorTools.ps1#  WmpInventorTools-$VERSION/Install-WmpInventorTools.ps1#" SHA256SUMS.txt) ) \
  || fail "SHA256SUMS.txt does not match the produced artifacts"
echo "release-test: SHA256SUMS OK"

# --- isolated install --------------------------------------------------------
ADDINS="$TEST_ROOT/addins"
STATE="$TEST_ROOT/state"
mkdir -p "$ADDINS" "$STATE"
ADDINS_W="$(cygpath -w "$ADDINS")"
STATE_W="$(cygpath -w "$STATE")"
ZIP_W="$(cygpath -w "$ROOT/$ZIP")"
SUMS_W="$(cygpath -w "$ROOT/$SUMS")"

pwsh -NoProfile -File "$INSTALLER" \
  -ZipPath "$ZIP_W" -Sha256SumsPath "$SUMS_W" \
  -AddinsRoot "$ADDINS_W" -StateRoot "$STATE_W" >/dev/null \
  || fail "the installer did not complete against the isolated roots"

[[ -f "$STATE/installed.json" ]] || fail "installed.json was not written"
grep -q "\"version\": \"$VERSION\"" "$STATE/installed.json" \
  || fail "installed.json does not record version $VERSION"

[[ -f "$STATE/Install-WmpInventorTools.ps1" ]] \
  || fail "Install-WmpInventorTools.ps1 was not persisted to the state root"
cmp -s "$STATE/Install-WmpInventorTools.ps1" "$ROOT/scripts/release/Install-WmpInventorTools.ps1" \
  || fail "the persisted installer is not byte-identical to scripts/release/Install-WmpInventorTools.ps1"
echo "release-test: persisted installer OK"

assert_manifest() {
  local manifest="$1" class_id="$2" assembly="$3"
  [[ -f "$ADDINS/$manifest" ]] || fail "$manifest was not written"
  pwsh -NoProfile -Command "
    \$ErrorActionPreference = 'Stop'
    [xml]\$m = Get-Content -Raw -LiteralPath '$(cygpath -w "$ADDINS/$manifest")'
    if (\$m.Addin.ClassId -ne '$class_id') { throw '$manifest has ClassId ' + \$m.Addin.ClassId }
    \$expected = [System.IO.Path]::GetFullPath('$(cygpath -w "$ADDINS/$assembly")')
    if (-not [string]::Equals([System.IO.Path]::GetFullPath(\$m.Addin.Assembly), \$expected, 'OrdinalIgnoreCase')) {
      throw '$manifest points at ' + \$m.Addin.Assembly
    }
    if (\$m.Addin.Assembly -notlike '*$(basename "$TEST_ROOT")*') { throw '$manifest escaped the isolated add-ins root' }
  " || fail "$manifest failed manifest validation"
}

assert_manifest "Autodesk.FileNamingManager.Inventor.addin" \
  "{BB7F1BFF-D45E-440A-897B-F70E6A4ADE67}" \
  "FileNamingManager/FileNamingManager.AddIn.dll"
assert_manifest "Autodesk.SmartManufacturingExporter.Inventor.addin" \
  "{A77D6A17-82A7-45C9-93C6-E6FA4EB91E73}" \
  "SmartManufacturingExporter/SmartManufacturingExporter.AddIn.dll"
echo "release-test: install and manifests OK"

# --- tampered digest ---------------------------------------------------------
BAD_SUMS="$TEST_ROOT/SHA256SUMS-bad.txt"
sed "s/^[0-9a-f]\{64\}/$(printf '0%.0s' {1..64})/" "$SUMS" > "$BAD_SUMS"
set +e
pwsh -NoProfile -File "$INSTALLER" \
  -ZipPath "$ZIP_W" -Sha256SumsPath "$(cygpath -w "$BAD_SUMS")" \
  -AddinsRoot "$ADDINS_W" -StateRoot "$STATE_W" >/dev/null 2>&1
bad_rc=$?
set -e
[[ "$bad_rc" -eq 5 ]] || fail "a tampered SHA256SUMS exited $bad_rc; expected 5"
echo "release-test: tampered digest rejected (exit 5) OK"

# --- idempotent reinstall ----------------------------------------------------
pwsh -NoProfile -File "$INSTALLER" \
  -ZipPath "$ZIP_W" -Sha256SumsPath "$SUMS_W" \
  -AddinsRoot "$ADDINS_W" -StateRoot "$STATE_W" >/dev/null \
  || fail "the second install did not complete"
[[ -d "$STATE/previous/$VERSION" ]] || fail "the second install did not archive the first one"
[[ -f "$STATE/previous/$VERSION/Autodesk.FileNamingManager.Inventor.addin" ]] \
  || fail "the archived install is missing its manifest"
echo "release-test: idempotent reinstall OK"

# --- rollback ----------------------------------------------------------------
pwsh -NoProfile -File "$INSTALLER" -Rollback \
  -AddinsRoot "$ADDINS_W" -StateRoot "$STATE_W" >/dev/null \
  || fail "rollback did not complete"

for restored in \
  "FileNamingManager/FileNamingManager.AddIn.dll" \
  "SmartManufacturingExporter/SmartManufacturingExporter.AddIn.dll" \
  "Autodesk.FileNamingManager.Inventor.addin" \
  "Autodesk.SmartManufacturingExporter.Inventor.addin"
do
  [[ -e "$ADDINS/$restored" ]] || fail "rollback did not restore $restored"
done
[[ -f "$STATE/installed.json" ]] || fail "rollback did not restore installed.json"
compgen -G "$STATE/previous/$VERSION-rolledback-*" >/dev/null \
  || fail "rollback did not archive the replaced install"
echo "release-test: rollback OK"

# --- terminal error, file mode: exit <code> is kept for callers that need it -
# A missing -ZipPath is the documented invalid-argument case (exit code 2).
FILEMODE_ADDINS="$TEST_ROOT/addins-filemode-error"
FILEMODE_STATE="$TEST_ROOT/state-filemode-error"
mkdir -p "$FILEMODE_ADDINS" "$FILEMODE_STATE"
FILEMODE_ADDINS_W="$(cygpath -w "$FILEMODE_ADDINS")"
FILEMODE_STATE_W="$(cygpath -w "$FILEMODE_STATE")"
FILEMODE_ZIP_W="$(cygpath -w "$TEST_ROOT/missing-filemode.zip")"

set +e
filemode_output="$(pwsh -NoProfile -File "$INSTALLER" \
  -ZipPath "$FILEMODE_ZIP_W" -Sha256SumsPath "$SUMS_W" \
  -AddinsRoot "$FILEMODE_ADDINS_W" -StateRoot "$FILEMODE_STATE_W" 2>&1)"
filemode_rc=$?
set -e
[[ "$filemode_rc" -eq 2 ]] || fail "a missing -ZipPath in file mode exited $filemode_rc; expected 2"
grep -q "does not exist" <<<"$filemode_output" \
  || fail "file-mode terminal error did not print the reason: $filemode_output"
echo "release-test: file-mode terminal error (exit 2, message printed) OK"

# --- terminal error, in-memory mode: the host session must survive ----------
# `exit N` inside `irm | iex` kills the host PowerShell window before a failed
# one-liner install can be read (verified: `pwsh -c "iex 'exit 3'; Write-Host after"`
# prints nothing). Stop-Install must instead Write-Host the reason, set
# $LASTEXITCODE, and `throw` so only the running script unwinds.
#
# A single `-Command "A; B"` string or a .ps1 run via -File both execute as one
# script unit, so an uncaught `throw` in A aborts B too - that is not a faithful
# stand-in for the real failure mode, where `irm | iex` is one interactive
# top-level entry and the NEXT command is a separate one the host reads after
# returning to its prompt. Piping separate statements to `pwsh -Command -`
# reproduces that: each line runs as its own top-level entry, so the session
# genuinely survives an uncaught throw in an earlier line, the way the real
# console host survives an uncaught throw in one `iex` line before reading the
# user's next one. The single-string form was tried first and does not print
# AFTER; do not "simplify" this back to it.
MEMMODE_ADDINS="$TEST_ROOT/addins-memmode-error"
MEMMODE_STATE="$TEST_ROOT/state-memmode-error"
mkdir -p "$MEMMODE_ADDINS" "$MEMMODE_STATE"
MEMMODE_ADDINS_W="$(cygpath -w "$MEMMODE_ADDINS")"
MEMMODE_STATE_W="$(cygpath -w "$MEMMODE_STATE")"
MEMMODE_ZIP_W="$(cygpath -w "$TEST_ROOT/missing-memmode.zip")"

memmode_output="$(cat <<EOS | pwsh -NoProfile -NoLogo -NonInteractive -Command - 2>&1
\$s = Get-Content -Raw -LiteralPath '$INSTALLER'
& ([scriptblock]::Create(\$s)) -ZipPath '$MEMMODE_ZIP_W' -Sha256SumsPath '$SUMS_W' -AddinsRoot '$MEMMODE_ADDINS_W' -StateRoot '$MEMMODE_STATE_W'
Write-Host "AFTER"
Write-Host "LASTEXITCODE=\$LASTEXITCODE"
exit 0
EOS
)"
grep -q "does not exist" <<<"$memmode_output" \
  || fail "in-memory terminal error did not print the reason: $memmode_output"
grep -q "Nothing was changed" <<<"$memmode_output" \
  || fail "in-memory terminal error did not print the safe-to-retry line: $memmode_output"
grep -qx "AFTER" <<<"$memmode_output" \
  || fail "in-memory terminal error closed the session; AFTER did not print: $memmode_output"
grep -qx "LASTEXITCODE=2" <<<"$memmode_output" \
  || fail "in-memory terminal error did not set \$LASTEXITCODE to 2: $memmode_output"
echo "release-test: in-memory terminal error (session survives, \$LASTEXITCODE=2) OK"

echo "release-package: OK"
