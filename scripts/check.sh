#!/usr/bin/env bash
# check.sh - canonical local and CI verification for the Inventor Scripts workspace.
# Usage: ./scripts/check.sh [--full-mutation]
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

FULL_MUTATION=0
for arg in "$@"; do
  case "$arg" in
    --full-mutation) FULL_MUTATION=1 ;;
    *) echo "unknown flag: $arg (supported: --full-mutation)" >&2; exit 2 ;;
  esac
done

fail() { echo "CHECK FAILED: $*" >&2; exit 1; }
need() { command -v "$1" >/dev/null 2>&1 || fail "$1 is required but was not found on PATH"; }

# Step 1: kernel integrity.
./scripts/kernel-hash.sh --verify

# Step 2: AGENTS.md line budget.
lines="$(wc -l < AGENTS.md)"
if (( lines > 180 )); then
  fail "AGENTS.md is $lines lines; the hard budget is 180"
fi
echo "line-budget: OK (AGENTS.md = $lines/180)"

# Step 3: untracked-files report.
untracked="$(git status --porcelain | grep '^??' || true)"
if [[ -n "$untracked" ]]; then
  echo "WARNING: untracked files exist; stage or ignore each one deliberately:"
  echo "$untracked" | sed 's/^?? /  /'
fi

[[ -f .start-done ]] || fail ".start-done is missing; initialization is incomplete"
need dotnet
need gitleaks
need git-cliff

# Step 4: workspace registration and project contracts.
while IFS= read -r project_dir; do
  [[ -f "$project_dir/README.md" ]] \
    || fail "$project_dir is missing its project README"
done < <(find projects -mindepth 1 -maxdepth 1 -type d -print | sort)

registered_projects="$(dotnet sln InventorScripts.sln list \
  | tail -n +3 \
  | tr '\\' '/' \
  | sed 's/\r$//' \
  | sort)"
discovered_projects="$(find projects shared -type f -name '*.csproj' -print \
  | sed 's#^\./##' \
  | sort)"
[[ "$registered_projects" == "$discovered_projects" ]] \
  || fail "InventorScripts.sln does not contain every .NET project under projects/ and shared/"

# Step 5: frozen restore and local tools.
dotnet tool restore
dotnet restore InventorScripts.sln --locked-mode

# Step 6: format check.
dotnet format InventorScripts.sln --verify-no-changes --no-restore

# Step 7: typecheck through a full Debug compilation with warnings as errors.
dotnet build InventorScripts.sln -c Debug --no-restore

# Step 8: analyzer lint.
dotnet format analyzers InventorScripts.sln --verify-no-changes --no-restore

# Step 9: all registered .NET tests and project-local script checks.
dotnet test InventorScripts.sln -c Debug --no-build --no-restore
shopt -s nullglob
for project_check in projects/*/scripts/check.sh; do
  bash "$project_check"
done
shopt -u nullglob

# Step 10: secret scan over Git history and the working tree.
gitleaks git --config .gitleaks.toml --redact --no-banner
gitleaks dir . --config .gitleaks.toml --redact --no-banner

# Step 11: production build.
dotnet build InventorScripts.sln -c Release --no-restore

# Step 12: project-owned mutation testing for changed production C# files, or all files on request.
mutation_base=""
if git rev-parse --verify origin/main >/dev/null 2>&1; then
  mutation_base="$(git merge-base HEAD origin/main || true)"
elif git rev-parse --verify HEAD^ >/dev/null 2>&1; then
  mutation_base="HEAD^"
fi

mutation_runs=0
for unit_dir in projects/* shared/*; do
  [[ -d "$unit_dir/src" ]] || continue
  find "$unit_dir/src" -type f -name '*.cs' -print -quit | grep -q . || continue

  mutation_check="$unit_dir/scripts/mutation.sh"
  [[ -f "$mutation_check" ]] \
    || fail "$unit_dir contains production C# but has no scripts/mutation.sh"

  mutation_needed=$FULL_MUTATION
  if (( ! mutation_needed )) && [[ -n "$mutation_base" ]] \
      && git diff --name-only "$mutation_base"...HEAD -- "$unit_dir/src" | grep -Eq '\.cs$'; then
    mutation_needed=1
  fi
  if (( ! mutation_needed )) \
      && git status --porcelain --untracked-files=all -- "$unit_dir/src" | grep -Eq '\.cs$'; then
    mutation_needed=1
  fi

  if (( mutation_needed )); then
    bash "$mutation_check"
    mutation_runs=$((mutation_runs + 1))
  fi
done

if (( ! mutation_runs )); then
  echo "mutation: skipped - no production C# changes relative to ${mutation_base:-the current tree}"
fi

# Step 13: changelog release-boundary regression.
bash ./scripts/test-changelog.sh

# Step 14: generated changelog freshness.
[[ -f CHANGELOG.md ]] || fail "CHANGELOG.md is missing; regenerate it with git-cliff"
changelog_tmp="$(mktemp)"
trap 'rm -f "$changelog_tmp"' EXIT
bash ./scripts/update-changelog.sh "$changelog_tmp"
diff -u CHANGELOG.md "$changelog_tmp" \
  || fail "CHANGELOG.md is stale; regenerate it with ./scripts/update-changelog.sh"

echo "check: OK"
