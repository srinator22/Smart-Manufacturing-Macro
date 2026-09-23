#!/usr/bin/env bash
# Exercises check-live-evidence.sh's failure modes against isolated fixture stamps, plus the
# real committed stamp against the real tree. The checker's sourceHash is always computed from
# the actual git-tracked sources (see check-live-evidence.sh), so only the stamp path - never the
# source tree - needs to be faked here.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$ROOT"

CHECKER="projects/file-naming-manager/scripts/check-live-evidence.sh"
REAL_STAMP="projects/file-naming-manager/tests/live-evidence/LIVE_EVIDENCE.json"

fixture_dir="$(mktemp -d)"
trap 'rm -rf "$fixture_dir"' EXIT

expect_fail() {
  local label="$1" stamp="$2" needle="$3"
  local output
  if output="$(bash "$CHECKER" "$stamp" 2>&1)"; then
    echo "LIVE-EVIDENCE TEST FAILED: expected failure for $label, but the checker passed:" >&2
    echo "$output" >&2
    exit 1
  fi
  grep -Fq "$needle" <<<"$output" \
    || { echo "LIVE-EVIDENCE TEST FAILED: $label output missing '$needle':" >&2; echo "$output" >&2; exit 1; }
}

# (1) missing stamp file - failure names the exact path.
missing_stamp="$fixture_dir/does-not-exist.json"
expect_fail "missing stamp file" "$missing_stamp" "LIVE EVIDENCE MISSING: $missing_stamp does not exist"

# (2) malformed JSON - no quoted result field for the field() extractor to find.
malformed_stamp="$fixture_dir/malformed.json"
printf '%s\n' '{ this is not valid json and has no result field' > "$malformed_stamp"
expect_fail "malformed JSON" "$malformed_stamp" "LIVE EVIDENCE MALFORMED: $malformed_stamp has no result field"

# (3) result present but not PASS.
not_pass_stamp="$fixture_dir/not-pass.json"
printf '%s\n' '{"result": "FAIL", "sourceHash": "deadbeef"}' > "$not_pass_stamp"
expect_fail "result not PASS" "$not_pass_stamp" "LIVE EVIDENCE NOT PASS: $not_pass_stamp records result='FAIL'"

# (4) stale sourceHash - a well-formed PASS stamp whose recorded hash cannot match the real
# tree's computed hash because it is a fixed, arbitrary 64-hex value.
stale_stamp="$fixture_dir/stale.json"
stale_hash="$(printf '0%.0s' {1..64})"
printf '{"result": "PASS", "sourceHash": "%s"}\n' "$stale_hash" > "$stale_stamp"
expect_fail "stale sourceHash" "$stale_stamp" "LIVE EVIDENCE STALE: one or more of these sources changed"

# (5) the real committed stamp against the real tree - must still pass with no argument
# (default stamp path) and with the default path given explicitly.
bash "$CHECKER" >/dev/null
bash "$CHECKER" "$REAL_STAMP" >/dev/null

echo "live-evidence-test: OK"
