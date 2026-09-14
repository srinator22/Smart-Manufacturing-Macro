#!/usr/bin/env bash
# Validates the minimum operator documentation contract for every project.
set -euo pipefail

ROOT="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
projects_root="$ROOT/projects"

fail() { echo "PROJECT README FAILED: $*" >&2; exit 1; }

[[ -d "$projects_root" ]] || fail "$projects_root does not exist"

required_sections=(
  "## What it does"
  "## Requirements"
  "## Install"
  "## Verify installation"
  "## Use"
  "## Update"
  "## Uninstall"
  "## Troubleshooting"
  "## Versioning and changelog"
  "## Known limitations"
  "## Development and verification"
)

project_count=0
while IFS= read -r project_dir; do
  project_count=$((project_count + 1))
  readme="$project_dir/README.md"
  [[ -f "$readme" ]] || fail "$project_dir is missing README.md"

  for section in "${required_sections[@]}"; do
    awk -v required="$section" '
      function trim_markdown_indent(line, count) {
        if (substr(line, 1, 1) == "\t") {
          return ""
        }

        count = 0
        while (count < 3 && substr(line, 1, 1) == " ") {
          line = substr(line, 2)
          count++
        }

        return substr(line, 1, 1) == " " ? "" : line
      }

      function fence_character(line, normalized, marker, count) {
        normalized = trim_markdown_indent(line)
        marker = substr(normalized, 1, 1)
        if (marker != "`" && marker != "~") {
          return ""
        }

        count = 0
        while (substr(normalized, count + 1, 1) == marker) {
          count++
        }

        return count >= 3 ? marker : ""
      }

      function fence_length(line, normalized, marker, count) {
        normalized = trim_markdown_indent(line)
        marker = substr(normalized, 1, 1)
        count = 0
        while (substr(normalized, count + 1, 1) == marker) {
          count++
        }

        return count
      }

      function fence_remainder(line, normalized, marker, count) {
        normalized = trim_markdown_indent(line)
        marker = substr(normalized, 1, 1)
        count = fence_length(line)
        return substr(normalized, count + 1)
      }

      {
        marker = fence_character($0)
        marker_length = marker == "" ? 0 : fence_length($0)
        marker_remainder = marker == "" ? "" : fence_remainder($0)
        if (open_fence != "") {
          if (marker == open_fence && marker_length >= open_fence_length && marker_remainder !~ /[^ \t]/) {
            open_fence = ""
            open_fence_length = 0
          }
          next
        }

        if (marker != "") {
          if (marker != "`" || index(marker_remainder, "`") == 0) {
            open_fence = marker
            open_fence_length = marker_length
            next
          }
        }

        if (trim_markdown_indent($0) == required) {
          found = 1
          exit
        }
      }

      END { exit found ? 0 : 1 }
    ' "$readme" \
      || fail "$readme is missing required section: $section"
  done
done < <(find "$projects_root" -mindepth 1 -maxdepth 1 -type d -print | sort)

(( project_count > 0 )) || fail "$projects_root contains no project directories"
echo "project-readmes: OK ($project_count project(s))"
