#!/usr/bin/env bash
# test-release.sh - Asserts the release package and the installer behave as ADR-0005 requires.
#
# Purpose: Prove, without Inventor and without a GitHub release, that build-release.ps1 produces a
#   complete verifiable package and that Install-WmpInventorTools.ps1 installs, rejects a tampered
#   digest, is idempotent, rolls back, and never closes the host session of a `irm ... | iex` run.
# Inputs: the working tree; run from anywhere, the script resolves the repository root itself.
#   The expected plugin set is DERIVED from projects/*/plugin.json, never hard-coded, so a new
#   project that the release forgets to package fails this test instead of shipping missing.
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

# --- PowerShell helpers ------------------------------------------------------
# jq is not a dependency of this repo, so the JSON the assertions need is read by small pwsh
# scripts written here. They are kept as files rather than -Command strings so neither bash nor
# PowerShell quoting has to be escaped twice.
HELPERS="$TEST_ROOT/helpers"
mkdir -p "$HELPERS"

cat > "$HELPERS/plugins.ps1" <<'PS_EOF'
param([Parameter(Mandatory = $true)][string]$ProjectsRoot)
$ErrorActionPreference = 'Stop'

# One tab-separated row per projects/*/plugin.json:
#   id, installDirectory, assembly, manifestName, maturity, ClassId
# manifestName and ClassId come from the project's own .addin template, which is the same source
# build-release.ps1 reads, so the test cannot drift from the packager by agreeing with itself.
Get-ChildItem -LiteralPath $ProjectsRoot -Directory | Sort-Object FullName | ForEach-Object {
    $catalogFile = Join-Path $_.FullName 'plugin.json'
    if (-not (Test-Path -LiteralPath $catalogFile -PathType Leaf)) { return }

    $definition = Get-Content -Raw -LiteralPath $catalogFile | ConvertFrom-Json
    $template = Join-Path $_.FullName $definition.addinTemplate
    if (-not (Test-Path -LiteralPath $template -PathType Leaf)) {
        throw "'$catalogFile' points at a missing manifest template '$template'."
    }

    [xml]$manifest = Get-Content -Raw -LiteralPath $template
    $row = @(
        $definition.id,
        $definition.installDirectory,
        $definition.assembly,
        [System.IO.Path]::GetFileNameWithoutExtension($template),
        $definition.maturity,
        $manifest.Addin.ClassId)
    $row -join "`t"
}
PS_EOF

cat > "$HELPERS/assert-catalog.ps1" <<'PS_EOF'
param(
    [Parameter(Mandatory = $true)][string]$CatalogPath,
    [Parameter(Mandatory = $true)][string]$Id,
    [Parameter(Mandatory = $true)][string]$InstallDirectory,
    [Parameter(Mandatory = $true)][string]$Assembly,
    [Parameter(Mandatory = $true)][string]$ManifestName,
    [Parameter(Mandatory = $true)][string]$Maturity,
    [Parameter(Mandatory = $true)][int]$ExpectedCount)
$ErrorActionPreference = 'Stop'

$catalog = Get-Content -Raw -LiteralPath $CatalogPath | ConvertFrom-Json
$all = @($catalog.plugins)
if ($all.Count -ne $ExpectedCount) {
    throw "catalog.json lists $($all.Count) plugins; the working tree declares $ExpectedCount."
}

$matched = @($all | Where-Object { $_.id -eq $Id })
if ($matched.Count -ne 1) { throw "catalog.json has $($matched.Count) entries for '$Id'; expected 1." }
$entry = $matched[0]

if ($entry.maturity -ne $Maturity) { throw "catalog.json records maturity '$($entry.maturity)' for '$Id'; expected '$Maturity'." }
if ($entry.installDirectory -ne $InstallDirectory) { throw "catalog.json records installDirectory '$($entry.installDirectory)' for '$Id'; expected '$InstallDirectory'." }
if ($entry.assembly -ne $Assembly) { throw "catalog.json records assembly '$($entry.assembly)' for '$Id'; expected '$Assembly'." }
if ($entry.manifestName -ne $ManifestName) { throw "catalog.json records manifestName '$($entry.manifestName)' for '$Id'; expected '$ManifestName'." }
if ($entry.addinTemplate -ne "templates/$Id.addin.template") { throw "catalog.json records addinTemplate '$($entry.addinTemplate)' for '$Id'; expected 'templates/$Id.addin.template'." }
PS_EOF

cat > "$HELPERS/assert-installed.ps1" <<'PS_EOF'
param(
    [Parameter(Mandatory = $true)][string]$StatePath,
    [Parameter(Mandatory = $true)][int]$ExpectedCount)
$ErrorActionPreference = 'Stop'

$state = Get-Content -Raw -LiteralPath $StatePath | ConvertFrom-Json
$plugins = @($state.plugins)
if ($plugins.Count -ne $ExpectedCount) {
    throw "installed.json lists $($plugins.Count) plugins; expected $ExpectedCount."
}

foreach ($plugin in $plugins) {
    if (-not $plugin.id) { throw "installed.json has a plugin entry with no id." }
    if (-not $plugin.maturity) { throw "installed.json plugin '$($plugin.id)' has no maturity." }
    # Rollback archives what installed.json says this install put in the Addins root, so both
    # location fields must be recorded or a newer plugin survives a rollback.
    if (-not $plugin.installDirectory) { throw "installed.json plugin '$($plugin.id)' has no installDirectory." }
    if (-not $plugin.manifestName) { throw "installed.json plugin '$($plugin.id)' has no manifestName." }
}
PS_EOF

cat > "$HELPERS/plant-fake-plugin.ps1" <<'PS_EOF'
param(
    [Parameter(Mandatory = $true)][string]$AddinsRoot,
    [Parameter(Mandatory = $true)][string]$StatePath)
$ErrorActionPreference = 'Stop'

# Stands in for a plugin that the NEWER release introduced and the archived older release never
# had. Rollback must archive it, not leave it installed and loaded under an older installed.json.
$fakeDirectory = Join-Path $AddinsRoot 'FakePlugin'
New-Item -ItemType Directory -Path $fakeDirectory -Force | Out-Null
[System.IO.File]::WriteAllText((Join-Path $fakeDirectory 'Fake.dll'), 'fake-plugin-bytes')
[System.IO.File]::WriteAllText(
    (Join-Path $AddinsRoot 'Autodesk.FakePlugin.Inventor.addin'),
    '<Addin Type="Standard"><ClassId>{0F0A0E0B-0000-0000-0000-00000000FA4E}</ClassId></Addin>')

$state = Get-Content -Raw -LiteralPath $StatePath | ConvertFrom-Json
$state.plugins = @(@($state.plugins) + [pscustomobject][ordered]@{
    id               = 'fake-plugin'
    maturity         = 'beta'
    installDirectory = 'FakePlugin'
    manifestName     = 'Autodesk.FakePlugin.Inventor.addin'
})
[System.IO.File]::WriteAllText(
    $StatePath,
    (ConvertTo-Json -InputObject $state -Depth 6),
    [System.Text.UTF8Encoding]::new($false))
PS_EOF

cat > "$HELPERS/compress.ps1" <<'PS_EOF'
param(
    [Parameter(Mandatory = $true)][string]$StageRoot,
    [Parameter(Mandatory = $true)][string]$ZipPath)
$ErrorActionPreference = 'Stop'

if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
Compress-Archive -Path (Join-Path $StageRoot '*') -DestinationPath $ZipPath -CompressionLevel Optimal
PS_EOF

ps_run() {
  local script="$1"; shift
  pwsh -NoProfile -File "$(cygpath -w "$HELPERS/$script")" "$@"
}

echo "release-test: building Release once"
dotnet restore InventorScripts.sln --locked-mode >/dev/null
dotnet build InventorScripts.sln -c Release --no-restore >/dev/null

echo "release-test: packaging $VERSION"
pwsh -NoProfile -File scripts/release/build-release.ps1 -Version "$VERSION" -SkipBuild >/dev/null

ZIP="dist/WmpInventorTools-$VERSION.zip"
SUMS="dist/SHA256SUMS.txt"
[[ -f "$ZIP" ]] || fail "$ZIP was not produced"
[[ -f "$SUMS" ]] || fail "$SUMS was not produced"

# --- expected plugin set, derived from the working tree ----------------------
PLUGIN_ROWS="$(ps_run plugins.ps1 -ProjectsRoot "$(cygpath -w "$ROOT/projects")" | tr -d '\r')" \
  || fail "could not read the plugin catalogs under projects/"
PLUGIN_ROWS="$(grep . <<<"$PLUGIN_ROWS" || true)"
[[ -n "$PLUGIN_ROWS" ]] || fail "no projects/*/plugin.json catalogs were found"
PLUGIN_COUNT="$(grep -c . <<<"$PLUGIN_ROWS")"
echo "release-test: $PLUGIN_COUNT plugin catalogs declared by the working tree"

unzip -p "$ZIP" catalog.json > "$TEST_ROOT/catalog.json" \
  || fail "catalog.json could not be extracted from $ZIP"

# --- package contents --------------------------------------------------------
ENTRIES="$(unzip -Z1 "$ZIP")"
for required in \
  "catalog.json" \
  "Install-WmpInventorTools.ps1"
do
  grep -Fxq "$required" <<<"$ENTRIES" || fail "the package does not contain $required"
done

while IFS=$'\t' read -r p_id p_dir p_asm p_manifest p_maturity p_classid; do
  [[ -n "$p_id" ]] || continue
  grep -Fxq "$p_dir/$p_asm" <<<"$ENTRIES" \
    || fail "the package does not contain $p_dir/$p_asm for plugin '$p_id'"
  grep -Fxq "templates/$p_id.addin.template" <<<"$ENTRIES" \
    || fail "the package does not contain templates/$p_id.addin.template for plugin '$p_id'"
  ps_run assert-catalog.ps1 \
    -CatalogPath "$(cygpath -w "$TEST_ROOT/catalog.json")" \
    -Id "$p_id" -InstallDirectory "$p_dir" -Assembly "$p_asm" \
    -ManifestName "$p_manifest" -Maturity "$p_maturity" -ExpectedCount "$PLUGIN_COUNT" \
    || fail "catalog.json does not describe plugin '$p_id' as projects/ declares it"
done <<<"$PLUGIN_ROWS"
echo "release-test: package contents OK ($PLUGIN_COUNT plugins packaged)"

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

while IFS=$'\t' read -r p_id p_dir p_asm p_manifest p_maturity p_classid; do
  [[ -n "$p_id" ]] || continue
  assert_manifest "$p_manifest" "$p_classid" "$p_dir/$p_asm"
done <<<"$PLUGIN_ROWS"

ps_run assert-installed.ps1 -StatePath "$STATE_W\\installed.json" -ExpectedCount "$PLUGIN_COUNT" \
  || fail "installed.json does not record every declared plugin with its maturity"
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
FIRST_MANIFEST="$(head -n1 <<<"$PLUGIN_ROWS" | cut -f4)"
[[ -f "$STATE/previous/$VERSION/$FIRST_MANIFEST" ]] \
  || fail "the archived install is missing its manifest $FIRST_MANIFEST"
echo "release-test: idempotent reinstall OK"

# --- rollback ----------------------------------------------------------------
# A plugin the newer release introduced is not present in the archived older release. Rollback must
# still archive it, or Inventor keeps loading it while installed.json reports the older version.
ps_run plant-fake-plugin.ps1 -AddinsRoot "$ADDINS_W" -StatePath "$STATE_W\\installed.json" \
  || fail "could not plant the newer-release-only plugin"
[[ -f "$ADDINS/FakePlugin/Fake.dll" ]] || fail "the planted plugin was not created"

pwsh -NoProfile -File "$INSTALLER" -Rollback \
  -AddinsRoot "$ADDINS_W" -StateRoot "$STATE_W" >/dev/null \
  || fail "rollback did not complete"

while IFS=$'\t' read -r p_id p_dir p_asm p_manifest p_maturity p_classid; do
  [[ -n "$p_id" ]] || continue
  [[ -e "$ADDINS/$p_dir/$p_asm" ]] || fail "rollback did not restore $p_dir/$p_asm"
  [[ -e "$ADDINS/$p_manifest" ]] || fail "rollback did not restore $p_manifest"
done <<<"$PLUGIN_ROWS"
[[ -f "$STATE/installed.json" ]] || fail "rollback did not restore installed.json"
ROLLED_BACK="$(compgen -G "$STATE/previous/$VERSION-rolledback-*" | head -n1)" \
  || fail "rollback did not archive the replaced install"

[[ ! -e "$ADDINS/FakePlugin" ]] \
  || fail "rollback left the newer-release-only plugin folder installed"
[[ ! -e "$ADDINS/Autodesk.FakePlugin.Inventor.addin" ]] \
  || fail "rollback left the newer-release-only plugin manifest installed"
[[ -f "$ROLLED_BACK/FakePlugin/Fake.dll" ]] \
  || fail "rollback deleted the newer-release-only plugin instead of archiving it"
[[ -f "$ROLLED_BACK/Autodesk.FakePlugin.Inventor.addin" ]] \
  || fail "rollback deleted the newer-release-only manifest instead of archiving it"
grep -qx "fake-plugin-bytes" "$ROLLED_BACK/FakePlugin/Fake.dll" \
  || fail "the archived newer-release-only plugin does not hold its original bytes"
echo "release-test: rollback OK (newer-release-only plugin archived, not stranded)"

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

# --- in-memory mode harness --------------------------------------------------
# `exit N` inside `irm | iex` kills the host PowerShell window before the one-liner's own output
# can be read (verified: `pwsh -c "iex 'exit 3'; Write-Host after"` prints nothing). That is true
# of the SUCCESS path as well as the failure paths, so the installer keeps `exit` for file mode
# only and otherwise falls off the end of the script.
#
# A single `-Command "A; B"` string or a .ps1 run via -File both execute as one script unit, so an
# uncaught `throw` in A aborts B too - that is not a faithful stand-in for the real failure mode,
# where `irm | iex` is one interactive top-level entry and the NEXT command is a separate one the
# host reads after returning to its prompt. Piping separate statements to `pwsh -Command -`
# reproduces that: each line runs as its own top-level entry, so the session genuinely survives an
# uncaught throw in an earlier line, the way the real console host survives an uncaught throw in
# one `iex` line before reading the user's next one. The single-string form was tried first and
# does not print AFTER; do not "simplify" this back to it.
run_in_memory() {
  cat <<EOS | pwsh -NoProfile -NoLogo -NonInteractive -Command - 2>&1
\$s = Get-Content -Raw -LiteralPath '$INSTALLER'
& ([scriptblock]::Create(\$s)) $*
Write-Host "AFTER"
Write-Host "LASTEXITCODE=\$LASTEXITCODE"
exit 0
EOS
}

# --- terminal error, in-memory mode: the host session must survive ----------
MEMMODE_ADDINS="$TEST_ROOT/addins-memmode-error"
MEMMODE_STATE="$TEST_ROOT/state-memmode-error"
mkdir -p "$MEMMODE_ADDINS" "$MEMMODE_STATE"
MEMMODE_ADDINS_W="$(cygpath -w "$MEMMODE_ADDINS")"
MEMMODE_STATE_W="$(cygpath -w "$MEMMODE_STATE")"
MEMMODE_ZIP_W="$(cygpath -w "$TEST_ROOT/missing-memmode.zip")"

memmode_output="$(run_in_memory "-ZipPath '$MEMMODE_ZIP_W' -Sha256SumsPath '$SUMS_W' -AddinsRoot '$MEMMODE_ADDINS_W' -StateRoot '$MEMMODE_STATE_W'")"
grep -q "does not exist" <<<"$memmode_output" \
  || fail "in-memory terminal error did not print the reason: $memmode_output"
grep -q "Nothing was changed" <<<"$memmode_output" \
  || fail "in-memory terminal error did not print the safe-to-retry line: $memmode_output"
grep -qx "AFTER" <<<"$memmode_output" \
  || fail "in-memory terminal error closed the session; AFTER did not print: $memmode_output"
grep -qx "LASTEXITCODE=2" <<<"$memmode_output" \
  || fail "in-memory terminal error did not set \$LASTEXITCODE to 2: $memmode_output"
echo "release-test: in-memory terminal error (session survives, \$LASTEXITCODE=2) OK"

# --- success, in-memory mode: the host session must survive too --------------
MEMOK_ADDINS="$TEST_ROOT/addins-memmode-ok"
MEMOK_STATE="$TEST_ROOT/state-memmode-ok"
mkdir -p "$MEMOK_ADDINS" "$MEMOK_STATE"
MEMOK_ADDINS_W="$(cygpath -w "$MEMOK_ADDINS")"
MEMOK_STATE_W="$(cygpath -w "$MEMOK_STATE")"
MEMOK_ARGS="-ZipPath '$ZIP_W' -Sha256SumsPath '$SUMS_W' -AddinsRoot '$MEMOK_ADDINS_W' -StateRoot '$MEMOK_STATE_W'"

memok_output="$(run_in_memory "$MEMOK_ARGS")"
grep -q "WMP Inventor Tools $VERSION is installed." <<<"$memok_output" \
  || fail "the in-memory install did not print its summary: $memok_output"
grep -qx "AFTER" <<<"$memok_output" \
  || fail "a successful in-memory install closed the session; AFTER did not print: $memok_output"
grep -qE "^LASTEXITCODE=0?$" <<<"$memok_output" \
  || fail "a successful in-memory install left a non-zero \$LASTEXITCODE: $memok_output"
[[ -f "$MEMOK_STATE/installed.json" ]] || fail "the in-memory install did not write installed.json"
echo "release-test: in-memory success (session survives, summary printed) OK"

# --- rollback, in-memory mode: the host session must survive too -------------
pwsh -NoProfile -File "$INSTALLER" \
  -ZipPath "$ZIP_W" -Sha256SumsPath "$SUMS_W" \
  -AddinsRoot "$MEMOK_ADDINS_W" -StateRoot "$MEMOK_STATE_W" >/dev/null \
  || fail "the second install into the in-memory roots did not complete"

memrb_output="$(run_in_memory "-Rollback -AddinsRoot '$MEMOK_ADDINS_W' -StateRoot '$MEMOK_STATE_W'")"
grep -q "is active" <<<"$memrb_output" \
  || fail "the in-memory rollback did not report the active version: $memrb_output"
grep -qx "AFTER" <<<"$memrb_output" \
  || fail "a successful in-memory rollback closed the session; AFTER did not print: $memrb_output"
grep -qE "^LASTEXITCODE=0?$" <<<"$memrb_output" \
  || fail "a successful in-memory rollback left a non-zero \$LASTEXITCODE: $memrb_output"
echo "release-test: in-memory rollback (session survives) OK"

# --- part-way failure: the reported state must be the true state -------------
# A package whose catalog lists a plugin folder the zip does not carry fails inside the install
# loop, after the Addins root has been touched. The message must not promise an archived previous
# version when there was none to archive.
BROKEN_DIR="$(tail -n1 <<<"$PLUGIN_ROWS" | cut -f2)"
[[ -n "$BROKEN_DIR" ]] || fail "could not determine a plugin folder to drop from the broken package"
BROKEN_STAGE="$TEST_ROOT/broken-stage"
cp -r "$ROOT/dist/WmpInventorTools-$VERSION" "$BROKEN_STAGE"
[[ -d "$BROKEN_STAGE/$BROKEN_DIR" ]] || fail "the staged package has no $BROKEN_DIR folder to drop"
rm -rf "${BROKEN_STAGE:?}/${BROKEN_DIR:?}"

BROKEN_ZIP_NAME="WmpInventorTools-$VERSION-broken.zip"
BROKEN_ZIP="$TEST_ROOT/$BROKEN_ZIP_NAME"
ps_run compress.ps1 -StageRoot "$(cygpath -w "$BROKEN_STAGE")" -ZipPath "$(cygpath -w "$BROKEN_ZIP")" \
  || fail "could not build the deliberately incomplete package"
BROKEN_SUMS="$TEST_ROOT/SHA256SUMS-broken.txt"
printf '%s  %s\n' "$(sha256sum "$BROKEN_ZIP" | cut -d' ' -f1)" "$BROKEN_ZIP_NAME" > "$BROKEN_SUMS"
BROKEN_ZIP_W="$(cygpath -w "$BROKEN_ZIP")"
BROKEN_SUMS_W="$(cygpath -w "$BROKEN_SUMS")"

PARTIAL1_ADDINS="$TEST_ROOT/addins-partial-fresh"
PARTIAL1_STATE="$TEST_ROOT/state-partial-fresh"
mkdir -p "$PARTIAL1_ADDINS" "$PARTIAL1_STATE"
partial1_output="$(run_in_memory "-ZipPath '$BROKEN_ZIP_W' -Sha256SumsPath '$BROKEN_SUMS_W' -AddinsRoot '$(cygpath -w "$PARTIAL1_ADDINS")' -StateRoot '$(cygpath -w "$PARTIAL1_STATE")'")"
grep -q "is missing the plugin folder '$BROKEN_DIR'" <<<"$partial1_output" \
  || fail "the incomplete package did not fail on the missing plugin folder: $partial1_output"
grep -q "there was no previous install to archive" <<<"$partial1_output" \
  || fail "a part-way failure with no previous install claimed one was archived: $partial1_output"
grep -qx "AFTER" <<<"$partial1_output" \
  || fail "a part-way failure closed the session; AFTER did not print: $partial1_output"
grep -qx "LASTEXITCODE=1" <<<"$partial1_output" \
  || fail "a part-way failure did not set \$LASTEXITCODE to 1: $partial1_output"
echo "release-test: part-way failure on a first install reports nothing archived OK"

PARTIAL2_ADDINS="$TEST_ROOT/addins-partial-upgrade"
PARTIAL2_STATE="$TEST_ROOT/state-partial-upgrade"
mkdir -p "$PARTIAL2_ADDINS" "$PARTIAL2_STATE"
PARTIAL2_ADDINS_W="$(cygpath -w "$PARTIAL2_ADDINS")"
PARTIAL2_STATE_W="$(cygpath -w "$PARTIAL2_STATE")"
pwsh -NoProfile -File "$INSTALLER" \
  -ZipPath "$ZIP_W" -Sha256SumsPath "$SUMS_W" \
  -AddinsRoot "$PARTIAL2_ADDINS_W" -StateRoot "$PARTIAL2_STATE_W" >/dev/null \
  || fail "the good install before the part-way upgrade did not complete"

partial2_output="$(run_in_memory "-ZipPath '$BROKEN_ZIP_W' -Sha256SumsPath '$BROKEN_SUMS_W' -AddinsRoot '$PARTIAL2_ADDINS_W' -StateRoot '$PARTIAL2_STATE_W'")"
grep -q "is missing the plugin folder '$BROKEN_DIR'" <<<"$partial2_output" \
  || fail "the incomplete upgrade did not fail on the missing plugin folder: $partial2_output"
grep -q "run it with -Rollback to restore the previous version" <<<"$partial2_output" \
  || fail "a part-way failure over an existing install did not point at -Rollback: $partial2_output"
grep -qx "AFTER" <<<"$partial2_output" \
  || fail "a part-way upgrade failure closed the session; AFTER did not print: $partial2_output"
[[ -d "$PARTIAL2_STATE/previous/$VERSION" ]] \
  || fail "a part-way upgrade failure did not leave the previous install archived"
echo "release-test: part-way failure over an existing install points at -Rollback OK"

echo "release-package: OK"
