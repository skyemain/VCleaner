#!/usr/bin/env bash
# Sets the next version in VERSION and rolls the "Unreleased" changelog section.
#
#   build/bump-version.sh patch|minor|major   bump the current version
#   build/bump-version.sh 1.4.0               set an explicit version
#   build/bump-version.sh minor --tag         also commit and create the git tag v<version>
#
# Development builds between releases automatically become <version>-dev.<commit count>; only releases change VERSION.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
arg="${1:?usage: bump-version.sh <major|minor|patch|X.Y.Z> [--tag]}"
tag="${2:-}"

current="$(tr -d '[:space:]' < VERSION)"
IFS=. read -r major minor patch <<< "$current"
case "$arg" in
  major) next="$((major + 1)).0.0" ;;
  minor) next="$major.$((minor + 1)).0" ;;
  patch) next="$major.$minor.$((patch + 1))" ;;
  *)
    [[ "$arg" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "Not a valid version: $arg" >&2; exit 2; }
    next="$arg" ;;
esac

echo "$next" > VERSION
today="$(date +%Y-%m-%d)"
if grep -q '^## \[Unreleased\]' CHANGELOG.md; then
  sed -i "s/^## \[Unreleased\]/## [Unreleased]\n\n## [$next] - $today/" CHANGELOG.md
fi
echo "$current -> $next"

if [ "$tag" = "--tag" ]; then
  git add VERSION CHANGELOG.md
  git commit -m "Release $next"
  git tag -a "v$next" -m "CleanSlate $next"
  echo "Created tag v$next (push with: git push origin main --tags)"
fi
