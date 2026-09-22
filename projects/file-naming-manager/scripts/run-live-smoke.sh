#!/usr/bin/env bash
# Runs the live Inventor smoke harness and, on a pass, refreshes the committed evidence stamp
# that check-live-evidence.sh gates on. Windows with Inventor 2027 only; refuses to start while
# any Inventor.exe is running, because the harness drives a hidden instance it owns and kills.
# The run log stays local under %TEMP%\naming-live-smoke\.
# Usage: bash projects/file-naming-manager/scripts/run-live-smoke.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$ROOT"

PROJECT="projects/file-naming-manager"
HARNESS="$PROJECT/tools/FileNamingManager.LiveSmoke"
EXE="$HARNESS/bin/Debug/net10.0-windows/FileNamingManager.LiveSmoke.exe"
STAMP="$PROJECT/tests/live-evidence/LIVE_EVIDENCE.json"

fail() { echo "$*" >&2; exit 1; }

command -v tasklist >/dev/null 2>&1 \
  || fail "REFUSED: tasklist was not found; the live smoke harness runs on Windows only."

# The double slash keeps MSYS from rewriting /FI into a path.
if tasklist //FI "IMAGENAME eq Inventor.exe" //NH 2>/dev/null | grep -qi 'Inventor\.exe'; then
  fail "REFUSED: Inventor.exe is running. Close every Inventor window and retry."
fi

dotnet build "$HARNESS/FileNamingManager.LiveSmoke.csproj" -c Debug

[[ -f "$EXE" ]] || fail "REFUSED: $EXE was not produced by the build."

status=0
timeout 900 "$EXE" || status=$?

if (( status == 124 )); then
  echo "TIMEOUT: the harness exceeded 900 s and was terminated." >&2
elif (( status == 0 )); then
  echo
  echo "$STAMP:"
  cat "$STAMP"
fi

exit "$status"
