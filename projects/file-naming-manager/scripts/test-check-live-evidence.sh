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
expect_fail "malformed JSON" "$malformed_stamp" "LIVE EVIDENCE MALFORMED: $malformed_stamp is not valid JSON"

# (2b) truncated JSON built from the REAL current sourceHash - both quoted fields the
# regex-based field() extractor looks for are present and correct, so only a real JSON
# syntax check (not regex field extraction) catches the missing closing brace.
real_hash="$(
  grep -o '"sourceHash"[[:space:]]*:[[:space:]]*"[^"]*"' "$REAL_STAMP" \
    | head -n 1 \
    | sed 's/.*"\([0-9a-f]*\)"$/\1/'
)"
[[ -n "$real_hash" ]] || { echo "LIVE-EVIDENCE TEST FAILED: could not read sourceHash from $REAL_STAMP" >&2; exit 1; }
truncated_stamp="$fixture_dir/truncated.json"
printf '{"result":"PASS","sourceHash":"%s"' "$real_hash" > "$truncated_stamp"
expect_fail "truncated JSON with the real current sourceHash" "$truncated_stamp" "LIVE EVIDENCE MALFORMED: $truncated_stamp is not valid JSON"

# (2c) syntactically valid JSON, but the record is wrapped in a top-level array rather
# than being the object the checker expects - the regex-based field() extractor would
# still find "result":"PASS" inside it, so only a real JSON structure check catches this.
array_stamp="$fixture_dir/array.json"
printf '[{"result":"PASS","sourceHash":"%s"}]' "$real_hash" > "$array_stamp"
expect_fail "PASS result wrapped in a top-level array" "$array_stamp" "LIVE EVIDENCE MALFORMED: $array_stamp is not valid JSON"

# (2d) syntactically valid JSON followed by a trailing garbage line - a strict JSON
# parser rejects the extra content even though the object itself is well-formed.
trailing_garbage_stamp="$fixture_dir/trailing-garbage.json"
printf '{"result":"PASS","sourceHash":"%s"}\nextra garbage line here\n' "$real_hash" > "$trailing_garbage_stamp"
expect_fail "valid JSON object followed by a trailing garbage line" "$trailing_garbage_stamp" "LIVE EVIDENCE MALFORMED: $trailing_garbage_stamp is not valid JSON"

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
