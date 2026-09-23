#!/usr/bin/env bash
# Exercises workflow_is_direct_to_main against the real AGENTS.md and isolated fixtures.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

# Sourcing runs only function definitions: new-task.sh guards its main body with
# `if [[ "${BASH_SOURCE[0]}" == "$0" ]]`, which is false when sourced.
source "$ROOT/scripts/new-task.sh"

fixture_dir="$(mktemp -d)"
trap 'rm -rf "$fixture_dir"' EXIT

expect_false() {
  local label="$1" file="$2"
  if workflow_is_direct_to_main "$file"; then
    echo "NEW-TASK TEST FAILED: expected false for $label" >&2
    exit 1
  fi
}

expect_true() {
  local label="$1" file="$2"
  if ! workflow_is_direct_to_main "$file"; then
    echo "NEW-TASK TEST FAILED: expected true for $label" >&2
    exit 1
  fi
}

# (a) the real, recorded workflow is branch + gated merge, not direct-to-main.
expect_false "real AGENTS.md" "$ROOT/AGENTS.md"

# (b) an explicit direct-to-main choice with a trailing parenthetical clause.
direct_file="$fixture_dir/direct.md"
printf '%s\n' '- Git workflow: direct-to-main (solo repository, no PR gate)' > "$direct_file"
expect_true "explicit direct-to-main choice with trailing clause" "$direct_file"

# (b2) the bare choice with nothing trailing at all.
bare_file="$fixture_dir/bare.md"
printf '%s\n' '- Git workflow: direct-to-main' > "$bare_file"
expect_true "bare direct-to-main choice" "$bare_file"

# (b3) the bare choice (mixed case) followed immediately by a semicolon clause.
semicolon_file="$fixture_dir/semicolon.md"
printf '%s\n' '- Git workflow: Direct-to-main; solo owner' > "$semicolon_file"
expect_true "direct-to-main choice with semicolon clause" "$semicolon_file"

# (c) branch + gated merge, with "direct-to-main" only mentioned incidentally later
# in the sentence - this is the exact false-positive the old grep matched.
mention_file="$fixture_dir/mention.md"
printf '%s\n' '- Git workflow: Branch plus pull request; direct-to-main is not a standing choice.' > "$mention_file"
expect_false "incidental mention of direct-to-main" "$mention_file"

# (d) the start.sh template placeholder line, never a recorded choice.
placeholder_file="$fixture_dir/placeholder.md"
printf '%s\n' '- Git workflow: {{branch + gated merge | direct-to-main}}' > "$placeholder_file"
expect_false "template placeholder" "$placeholder_file"

# (e) a negated decision: "direct-to-main" is discussed and rejected, not chosen.
# Before the fix, the prefix-only check ("$lower" == direct-to-main*) matched this
# and wrongly stayed on the current branch against the recorded workflow.
negated_file="$fixture_dir/negated.md"
printf '%s\n' '- Git workflow: direct-to-main is not permitted; use branch plus gated merge' > "$negated_file"
expect_false "negated direct-to-main decision" "$negated_file"

# (f) "direct-to-main" starts the sentence but is followed by a word, not a
# trailing-clause marker - a sentence about direct-to-main, not the choice itself.
sentence_file="$fixture_dir/sentence.md"
printf '%s\n' '- Git workflow: direct-to-main to a solo repository' > "$sentence_file"
expect_false "direct-to-main followed by a word, not a clause marker" "$sentence_file"

echo "new-task-test: OK"
