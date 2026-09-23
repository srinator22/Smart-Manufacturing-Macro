#!/usr/bin/env bash
# Product-local checks that are discovered by the workspace gauntlet.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$ROOT"

command -v pwsh >/dev/null 2>&1 \
  || { echo "CHECK FAILED: pwsh is required for the add-in packaging test" >&2; exit 1; }

pwsh -NoProfile -File projects/file-naming-manager/scripts/test-packaging.ps1

bash projects/file-naming-manager/scripts/test-check-live-evidence.sh

bash projects/file-naming-manager/scripts/check-live-evidence.sh
