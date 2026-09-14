#!/usr/bin/env bash
# Exercises the project README contract against isolated pass and fail fixtures.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
fixture_root="$(mktemp -d)"
fenced_fixture_root="$(mktemp -d)"
trap 'rm -rf "$fixture_root" "$fenced_fixture_root"' EXIT

mkdir -p "$fixture_root/projects/example"
printf '%s\n' \
  '# Example' \
  '## What it does' \
  '## Requirements' \
  '## Install' \
  '## Verify installation' \
  '## Use' \
  '## Update' \
  '## Uninstall' \
  '## Troubleshooting' \
  '## Versioning and changelog' \
  '## Known limitations' \
  '## Development and verification' \
  > "$fixture_root/projects/example/README.md"

bash "$ROOT/scripts/check-project-readmes.sh" "$fixture_root" >/dev/null

mkdir -p "$fenced_fixture_root/projects/fenced-example"
fenced_failure_output="$fenced_fixture_root/failure.txt"
for fence in '```' '~~~'; do
  printf '%s\n' \
    '# Fenced example' \
    "${fence}markdown" \
    '## What it does' \
    '## Requirements' \
    '## Install' \
    '## Verify installation' \
    '## Use' \
    '## Update' \
    '## Uninstall' \
    '## Troubleshooting' \
    '## Versioning and changelog' \
    '## Known limitations' \
    '## Development and verification' \
    "$fence" \
    > "$fenced_fixture_root/projects/fenced-example/README.md"

  if bash "$ROOT/scripts/check-project-readmes.sh" "$fenced_fixture_root" >"$fenced_failure_output" 2>&1; then
    echo "PROJECT README TEST FAILED: fenced fake headings unexpectedly passed for $fence" >&2
    exit 1
  fi
  grep -Fq 'missing required section: ## What it does' "$fenced_failure_output" \
    || { echo "PROJECT README TEST FAILED: fenced-heading error was not actionable" >&2; exit 1; }
done

for fence in '```' '~~~'; do
  printf '%s\n' \
    '# Pseudo-closer example' \
    "${fence}markdown" \
    "${fence}not-a-commonmark-close" \
    '## What it does' \
    '## Requirements' \
    '## Install' \
    '## Verify installation' \
    '## Use' \
    '## Update' \
    '## Uninstall' \
    '## Troubleshooting' \
    '## Versioning and changelog' \
    '## Known limitations' \
    '## Development and verification' \
    "$fence" \
    > "$fenced_fixture_root/projects/fenced-example/README.md"

  if bash "$ROOT/scripts/check-project-readmes.sh" "$fenced_fixture_root" >"$fenced_failure_output" 2>&1; then
    echo "PROJECT README TEST FAILED: pseudo-closer fake headings unexpectedly passed for $fence" >&2
    exit 1
  fi
  grep -Fq 'missing required section: ## What it does' "$fenced_failure_output" \
    || { echo "PROJECT README TEST FAILED: pseudo-closer error was not actionable" >&2; exit 1; }
done

sed -i '/^## Use$/d' "$fixture_root/projects/example/README.md"
failure_output="$fixture_root/failure.txt"
if bash "$ROOT/scripts/check-project-readmes.sh" "$fixture_root" >"$failure_output" 2>&1; then
  echo "PROJECT README TEST FAILED: incomplete README unexpectedly passed" >&2
  exit 1
fi
grep -Fq 'missing required section: ## Use' "$failure_output" \
  || { echo "PROJECT README TEST FAILED: missing-section error was not actionable" >&2; exit 1; }

echo "project-readme-test: OK"
