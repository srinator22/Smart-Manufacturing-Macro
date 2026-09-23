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

# (b) an explicit direct-to-main choice.
direct_file="$fixture_dir/direct.md"
printf '%s\n' '- Git workflow: direct-to-main to a solo repository' > "$direct_file"
expect_true "explicit direct-to-main choice" "$direct_file"

# (c) branch + gated merge, with "direct-to-main" only mentioned incidentally later
# in the sentence - this is the exact false-positive the old grep matched.
mention_file="$fixture_dir/mention.md"
printf '%s\n' '- Git workflow: Branch plus pull request; direct-to-main is not a standing choice.' > "$mention_file"
expect_false "incidental mention of direct-to-main" "$mention_file"

# (d) the start.sh template placeholder line, never a recorded choice.
placeholder_file="$fixture_dir/placeholder.md"
printf '%s\n' '- Git workflow: {{branch + gated merge | direct-to-main}}' > "$placeholder_file"
expect_false "template placeholder" "$placeholder_file"

echo "new-task-test: OK"
