#!/usr/bin/env bash
# Regression test for release-tag and synthetic pull-request merge stability.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

version="$(sed -n 's#.*<VersionPrefix>\([^<]*\)</VersionPrefix>.*#\1#p' Directory.Build.props)"
release_tag="v$version"
git rev-parse --quiet --verify "refs/tags/$release_tag" >/dev/null \
  || exit 0

tree="$(git write-tree)"
release_commit="$(git rev-list -n 1 "$release_tag")"
export GIT_AUTHOR_NAME="Changelog Regression Test"
export GIT_AUTHOR_EMAIL="changelog-test@example.invalid"
export GIT_COMMITTER_NAME="$GIT_AUTHOR_NAME"
export GIT_COMMITTER_EMAIL="$GIT_AUTHOR_EMAIL"
post_release_commit="$(printf '%s\n' 'chore(release): simulate post-release state' \
  | git commit-tree "$tree" -p HEAD)"
synthetic_merge="$(printf '%s\n' 'Merge pull request simulation' \
  | git commit-tree "$tree" -p "$release_commit" -p "$post_release_commit")"

actual="$(mktemp)"
trap 'rm -f "$actual"' EXIT
bash ./scripts/update-changelog.sh "$actual" "$synthetic_merge"
diff -u CHANGELOG.md "$actual" \
  || { echo "CHANGELOG TEST FAILED: synthetic merge output differs" >&2; exit 1; }
