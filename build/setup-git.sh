#!/usr/bin/env bash
# Idempotent: points a "github" remote at the project repository and lets git use the GITHUB_TOKEN (or GH_TOKEN)
# environment secret for github.com. The token is never written to disk or to git config; it is read from the environment
# each time git asks. Set the secret in Cursor Dashboard > Cloud Agents > Secrets.
#
#   build/setup-git.sh [https://github.com/OWNER/REPO.git]
set -euo pipefail

url="${1:-https://github.com/skyemain/VCleaner.git}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

if git remote get-url github >/dev/null 2>&1; then
  git remote set-url github "$url"
else
  git remote add github "$url"
fi

git config --global "credential.https://github.com.helper" \
  '!f() { test "$1" = get || exit 0; t="${GITHUB_TOKEN:-$GH_TOKEN}"; test -n "$t" || exit 0; echo username=x-access-token; echo "password=$t"; }; f'

if [[ -n "${GITHUB_TOKEN:-${GH_TOKEN:-}}" ]]; then
  echo "git is ready: git push github main"
else
  echo "Remote 'github' is set to $url, but no GITHUB_TOKEN secret is available in this session." >&2
  echo "Add one in Cursor Dashboard > Cloud Agents > Secrets (fine-grained token, Contents: read and write)." >&2
fi
