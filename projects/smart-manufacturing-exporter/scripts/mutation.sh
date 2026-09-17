#!/usr/bin/env bash
# Runs the product's mutation suite for each project that owns risky logic and
# rejects an empty, unscored, or below-threshold run. Fails on the first
# project that fails or drops below its threshold, and reports which one.
#
# Thresholds are `measured score - 5, rounded down to the nearest multiple of
# 5, floor 0`. Re-baseline deliberately (do not just widen the gap) when a
# genuine improvement raises the measured score.
#
#   Core:        measured 80.00% on 2026-09-17 -> 80-5=75.00 -> --break-at 75
#   Application: measured 85.11% on 2026-09-17 -> 85.11-5=80.11 -> --break-at 80
#
# UI (SmartManufacturingExporter.UI.csproj) is EXCLUDED from this loop.
# It targets net10.0-windows with UseWPF, and Stryker 5.0.0 cannot analyze it
# at all - its simulated build fails and Stryker aborts before creating any
# mutants:
#   "[WRN] Project ...SmartManufacturingExporter.UI.csproj simulated build
#    failed. Trying again with a nuget restore."
#   "[WRN] Analysis of project ...SmartManufacturingExporter.UI.csproj failed
#    for frameworks net10.0-windows."
#   "Failed to analyze project builds. Stryker cannot continue."
# (measured 2026-09-17, dotnet-stryker 5.0.0, exit code 1 in ~3s). This means
# the UI project's tri-state selection logic - the source of four prior
# review defects - is NOT mutation-tested by this script. That is a known,
# reported gap, not a silent drop: fixing it requires either a Stryker/UI
# project change (out of scope here) or extracting the selection logic into a
# plain net10.0 project Stryker can mutate.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$ROOT"

TEST_PROJECT="projects/smart-manufacturing-exporter/tests/SmartManufacturingExporter.UnitTests/SmartManufacturingExporter.UnitTests.csproj"

project_names=(Core Application)
project_paths=(
  "projects/smart-manufacturing-exporter/src/SmartManufacturingExporter.Core/SmartManufacturingExporter.Core.csproj"
  "projects/smart-manufacturing-exporter/src/SmartManufacturingExporter.Application/SmartManufacturingExporter.Application.csproj"
)
project_break_at=(75 80)

result_file="$(mktemp)"
trap 'rm -f "$result_file"' EXIT

for i in "${!project_names[@]}"; do
  name="${project_names[$i]}"
  project_path="${project_paths[$i]}"
  break_at="${project_break_at[$i]}"

  echo "== mutation: $name (--break-at $break_at) =="

  : > "$result_file"
  set +e
  dotnet stryker \
    --project "$project_path" \
    --test-project "$TEST_PROJECT" \
    --configuration Release --reporter ClearText --skip-version-check --break-at "$break_at" \
    2>&1 | tee "$result_file"
  stryker_status=${PIPESTATUS[0]}
  set -e

  if (( stryker_status != 0 )); then
    echo "MUTATION FAILED: project $name exited $stryker_status" >&2
    exit "$stryker_status"
  fi

  # Require positive evidence of a scored run. Matching Stryker's failure wording
  # instead would silently pass an unscored run whenever that wording changes
  # between versions; the score line is the thing this gate actually depends on.
  if ! grep -Eq 'The final mutation score is' "$result_file"; then
    echo "MUTATION FAILED: project $name did not report a mutation score" >&2
    exit 1
  fi
done
