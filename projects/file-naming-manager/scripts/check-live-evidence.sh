#!/usr/bin/env bash
# Fails when the committed live-evidence stamp no longer attests to the code in the tree.
# The stamp records the SHA-256 of the ordinally sorted "<relativePath>:<gitBlobId>" lines for
# every InventorAdapter source, the workflow Execute path, and the harness itself. Nothing but a
# live Inventor run can produce it, so a mismatch means live behaviour is unclaimed for the
# current sources. The hash rule here and in Program.cs.ComputeSourceHash must stay identical.
# Usage: bash projects/file-naming-manager/scripts/check-live-evidence.sh [stamp-path]
# stamp-path defaults to the project's committed LIVE_EVIDENCE.json; an explicit path is used
# for testing failure modes without disturbing the real stamp.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$ROOT"

PROJECT="projects/file-naming-manager"
STAMP="${1:-$PROJECT/tests/live-evidence/LIVE_EVIDENCE.json}"
REFRESH="run: bash projects/file-naming-manager/scripts/run-live-smoke.sh on a machine with Inventor 2027"

fail() { echo "$*" >&2; exit 1; }

# Reads one top-level string field without depending on jq.
# A missing key yields an empty string instead of killing the script under pipefail, so the
# callers below print their MALFORMED / NOT PASS messages rather than a bare exit 1.
field() {
  { grep -o "\"$1\"[[:space:]]*:[[:space:]]*\"[^\"]*\"" "$STAMP" || true; } \
    | head -n 1 \
    | sed 's/.*:[[:space:]]*"\(.*\)"$/\1/'
}

[[ -f "$STAMP" ]] || fail "LIVE EVIDENCE MISSING: $STAMP does not exist. $REFRESH"

# field() below extracts values with a regex, not a JSON parser: a truncated document (a
# stray closing brace missing) or a value wrapped in a top-level array still contains the
# right substrings and would otherwise be accepted. PowerShell 7 (pwsh) is already required
# by this gate, so ConvertFrom-Json performs a real syntax check - and -NoEnumerate keeps a
# single-element array from being silently unwrapped into what looks like a bare object -
# before any field extracted below is trusted.
STAMP_PATH="$STAMP" pwsh -NoProfile -Command '
  try {
    $raw = Get-Content -Raw -LiteralPath $env:STAMP_PATH -ErrorAction Stop
    $parsed = ConvertFrom-Json -InputObject $raw -NoEnumerate -ErrorAction Stop
    if ($parsed -is [System.Array]) { exit 1 }
    exit 0
  } catch {
    exit 1
  }
' >/dev/null 2>&1 \
  || fail "LIVE EVIDENCE MALFORMED: $STAMP is not valid JSON. $REFRESH"

result="$(field result)"
[[ -n "$result" ]] || fail "LIVE EVIDENCE MALFORMED: $STAMP has no result field. $REFRESH"
[[ "$result" == "PASS" ]] \
  || fail "LIVE EVIDENCE NOT PASS: $STAMP records result='$result'. $REFRESH"

recorded="$(field sourceHash)"
[[ -n "$recorded" ]] || fail "LIVE EVIDENCE MALFORMED: $STAMP has no sourceHash. $REFRESH"

# --cached --others keeps the check honest on a branch where the harness is not committed yet.
mapfile -t files < <(
  git ls-files --cached --others --exclude-standard -- \
    "$PROJECT/src/FileNamingManager.InventorAdapter/*.cs" \
    "$PROJECT/src/FileNamingManager.Application/FileNamingWorkflow.cs" \
    "$PROJECT/tools/FileNamingManager.LiveSmoke/Program.cs" \
    | grep -Ev '/(bin|obj)/' \
    | LC_ALL=C sort -u
)

(( ${#files[@]} > 0 )) || fail "LIVE EVIDENCE SOURCE SET EMPTY: no files matched under $PROJECT"

mapfile -t hashed < <(
  for file in "${files[@]}"; do
    printf '%s:%s\n' "${file#"$PROJECT/"}" "$(git hash-object -- "$file")"
  done | LC_ALL=C sort
)

# Command substitution strips the trailing newline, leaving exactly the LF-joined text the
# harness hashes.
joined="$(printf '%s\n' "${hashed[@]}")"
computed="$(printf '%s' "$joined" | sha256sum | cut -d' ' -f1)"

if [[ "$computed" != "$recorded" ]]; then
  {
    echo "LIVE EVIDENCE STALE: one or more of these sources changed since the last recorded live run:"
    printf '  %s\n' "${files[@]}"
    echo "  recorded sourceHash: $recorded"
    echo "  current  sourceHash: $computed"
    echo "$REFRESH"
  } >&2
  exit 1
fi

echo "live-evidence: OK (${#files[@]} source(s), sourceHash $computed)"
