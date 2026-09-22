#!/usr/bin/env bash
# Runs the product's mutation suite for each project that owns risky logic and
# rejects an empty, unscored, or below-threshold run. Fails on the first
# project that fails or drops below its threshold, and reports which one.
#
# Thresholds are `measured score - 5, rounded down to the nearest multiple of
# 5, floor 0`. Re-baseline deliberately (do not just widen the gap) when a
# genuine improvement raises the measured score.
#
#   Core:           measured 92.57% on 2026-09-23 -> 92.57-5=87.57 -> --break-at 85
#   Application:    measured 92.47% on 2026-09-23 -> 92.47-5=87.47 -> --break-at 85
#   Infrastructure: measured 79.31% on 2026-09-23 -> 79.31-5=74.31 -> --break-at 70
#
# Infrastructure sits lower than the other two on purpose, and the gap is
# understood rather than tolerated. Its remaining survivors are:
#   - four ConfigureAwait(false) -> ConfigureAwait(true) mutants, which behave
#     identically in a test host that installs no synchronisation context;
#   - the ProcessStartInfo UseShellExecute/CreateNoWindow flags, which nothing
#     outside the operating system can observe once the process has started;
#   - the Process.Start returned-null branch, which Windows does not produce;
#   - the IOException catch blocks of HasPreviousInstall and FindInstaller, and
#     FindInstaller's missing-staging-folder early return, which the same catch
#     makes equivalent. Killing them needs a directory that exists but refuses
#     enumeration - reproducible only by manipulating ACLs, which would make the
#     suite machine-dependent;
#   - the argument guards of CreateDirectory, WriteText and WritePendingApply,
#     whose removal changes nothing observable because Directory.CreateDirectory,
#     File.WriteAllText and PendingApplyJson.Serialize raise the same exception
#     type for the same input.
# Every other failure path of the adapters is exercised for real: a file another
# handle holds open, a cancelled request, a non-success status, one process that
# actually starts, and one that actually exits before its id is asked about.
#
# UI (WmpToolsManager.UI.csproj) and AddIn (WmpToolsManager.AddIn.csproj) are
# EXCLUDED from this loop. Like SmartManufacturingExporter.UI and
# FileNamingManager.UI (see the sibling projects' mutation.sh), the UI project
# targets net10.0-windows with UseWPF, and Stryker 5.0.0 cannot analyze it at
# all - its simulated build fails and Stryker aborts before creating any
# mutants. AddIn holds live Inventor COM interop and is not mutation-tested for
# the same class of tooling limitation. This is a known, reported gap, not a
# silent drop.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$ROOT"

TEST_PROJECT="projects/wmp-tools-manager/tests/WmpToolsManager.UnitTests/WmpToolsManager.UnitTests.csproj"

project_names=(Core Application Infrastructure)
project_paths=(
  "projects/wmp-tools-manager/src/WmpToolsManager.Core/WmpToolsManager.Core.csproj"
  "projects/wmp-tools-manager/src/WmpToolsManager.Application/WmpToolsManager.Application.csproj"
  "projects/wmp-tools-manager/src/WmpToolsManager.Infrastructure/WmpToolsManager.Infrastructure.csproj"
)
project_break_at=(85 85 70)

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
