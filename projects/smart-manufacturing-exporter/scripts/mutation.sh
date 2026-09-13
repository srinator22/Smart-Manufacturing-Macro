#!/usr/bin/env bash
# Runs the product's mutation suite and rejects an empty or unscored run.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$ROOT"

result_file="$(mktemp)"
trap 'rm -f "$result_file"' EXIT

set +e
dotnet stryker \
  --project projects/smart-manufacturing-exporter/src/SmartManufacturingExporter.Core/SmartManufacturingExporter.Core.csproj \
  --test-project projects/smart-manufacturing-exporter/tests/SmartManufacturingExporter.UnitTests/SmartManufacturingExporter.UnitTests.csproj \
  --configuration Release --reporter ClearText --skip-version-check --break-at 0 \
  2>&1 | tee "$result_file"
stryker_status=${PIPESTATUS[0]}
set -e

(( stryker_status == 0 )) || exit "$stryker_status"
if grep -Eiq '0 mutants created|unable to calculate a mutation score' "$result_file"; then
  echo "MUTATION FAILED: Stryker did not produce a scored mutation run" >&2
  exit 1
fi
