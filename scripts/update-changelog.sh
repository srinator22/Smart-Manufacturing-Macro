#!/usr/bin/env bash
# Generates a clone-independent changelog for the version declared by MSBuild.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

output="${1:-CHANGELOG.md}"
revision="${2:-}"
version="$(sed -n 's#.*<VersionPrefix>\([^<]*\)</VersionPrefix>.*#\1#p' Directory.Build.props)"
if [[ -z "$version" ]]; then
  echo "CHANGELOG FAILED: Directory.Build.props has no VersionPrefix" >&2
  exit 1
fi

release_tag="v$version"
raw_output="$(mktemp)"
trap 'rm -f "$raw_output"' EXIT

cliff_args=(--config cliff.toml --output "$raw_output")
if git rev-parse --quiet --verify "refs/tags/$release_tag" >/dev/null; then
  :
else
  cliff_args+=(--tag "$release_tag")
fi
if [[ -n "$revision" ]]; then
  cliff_args+=("$revision")
fi

git-cliff "${cliff_args[@]}"

# A GitHub pull-request merge ref can introduce an otherwise empty
# Unreleased section. Removing only a header immediately followed by the
# next release keeps meaningful unreleased entries intact.
sed -z -E 's/## \[Unreleased\](\r?\n)+## /## /' "$raw_output" > "$output"
