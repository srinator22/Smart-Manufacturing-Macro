#!/usr/bin/env bash
# Runs the product's mutation suite for each project that owns risky logic and
# rejects an empty, unscored, or below-threshold run. Fails on the first
# project that fails or drops below its threshold, and reports which one.
#
# Thresholds are `measured score - 5, rounded down to the nearest multiple of
# 5, floor 0`. Re-baseline deliberately (do not just widen the gap) when a
# genuine improvement raises the measured score.
#
#   Core:           measured 93.33% on 2026-09-23 -> 93.33-5=88.33 -> --break-at 85
#   Application:    measured 94.86% on 2026-09-23 -> 94.86-5=89.86 -> --break-at 85
#   Infrastructure: measured 97.67% on 2026-09-23 -> 97.67-5=92.67 -> --break-at 90
#
# UI (FileNamingManager.UI.csproj), InventorAdapter
# (FileNamingManager.InventorAdapter.csproj), and AddIn
# (FileNamingManager.AddIn.csproj) are EXCLUDED from this loop. Like
# SmartManufacturingExporter.UI (see
# ../../smart-manufacturing-exporter/scripts/mutation.sh), the UI project
# targets net10.0-windows with UseWPF, and Stryker 5.0.0 cannot analyze it at
# all - its simulated build fails and Stryker aborts before creating any
# mutants. InventorAdapter and AddIn hold live Inventor COM interop and are
# not mutation-tested for the same class of tooling limitation. This is a
# known, reported gap, not a silent drop.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$ROOT"

TEST_PROJECT="projects/file-naming-manager/tests/FileNamingManager.UnitTests/FileNamingManager.UnitTests.csproj"

project_names=(Core Application Infrastructure)
project_paths=(
  "projects/file-naming-manager/src/FileNamingManager.Core/FileNamingManager.Core.csproj"
  "projects/file-naming-manager/src/FileNamingManager.Application/FileNamingManager.Application.csproj"
  "projects/file-naming-manager/src/FileNamingManager.Infrastructure/FileNamingManager.Infrastructure.csproj"
)
project_break_at=(85 85 90)

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

  # A Safe Mode run rolls back the mutants that failed to compile and still prints a score, so the
  # score alone cannot show that every file was measured. NumberAllocator lost its coverage that way once.
  if grep -Eqi 'safe mode' "$result_file"; then
    echo "MUTATION FAILED: project $name ran in Stryker Safe Mode; some mutants were rolled back unmeasured" >&2
    exit 1
  fi
done
