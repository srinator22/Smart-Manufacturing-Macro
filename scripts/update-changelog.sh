#!/usr/bin/env bash
# Generates a clone-independent changelog for the version declared by MSBuild.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

output="${1:-CHANGELOG.md}"
version="$(sed -n 's#.*<VersionPrefix>\([^<]*\)</VersionPrefix>.*#\1#p' Directory.Build.props)"
if [[ -z "$version" ]]; then
  echo "CHANGELOG FAILED: Directory.Build.props has no VersionPrefix" >&2
  exit 1
fi

git-cliff --config cliff.toml --tag "v$version" --output "$output"
