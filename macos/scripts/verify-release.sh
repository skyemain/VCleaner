#!/bin/bash
# Checks a built app and, when present, the DMG. Refuses to invent a result when the artifacts are absent.
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "error: release verification must be run on macOS. This machine is $(uname -s)." >&2
  echo "No app bundle or DMG was verified." >&2
  exit 2
fi

app="${1:-build/CleanSlate.app}"
if [[ ! -d "${app}" ]]; then
  echo "error: ${app} does not exist, so there is nothing to verify." >&2
  exit 1
fi

plist="${app}/Contents/Info.plist"
version="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "${plist}")"
identifier="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "${plist}")"
echo "Bundle ${identifier} version ${version}"
test "${identifier}" = "com.skyemain.CleanSlate"
test -x "${app}/Contents/MacOS/CleanSlate"

archs="$(lipo -archs "${app}/Contents/MacOS/CleanSlate")"
echo "Architectures: ${archs}"
[[ "${archs}" == *arm64* && "${archs}" == *x86_64* ]] || {
  echo "error: the executable is not a universal arm64 and x86_64 binary (${archs})." >&2
  exit 1
}

if codesign --verify --deep --strict "${app}" 2>/dev/null; then
  echo "Signature: valid"
  codesign -dv --verbose=4 "${app}" 2>&1 | head -20
else
  echo "Signature: unsigned local build. This is not a notarized release."
fi

dmg="$(ls release/CleanSlate-*-mac-universal.dmg 2>/dev/null | head -1 || true)"
if [[ -n "${dmg}" ]]; then
  mount="$(mktemp -d)"
  hdiutil attach "${dmg}" -mountpoint "${mount}" -nobrowse >/dev/null
  test -d "${mount}/CleanSlate.app"
  test -L "${mount}/Applications"
  echo "DMG contains CleanSlate.app and the Applications shortcut: ${dmg}"
  hdiutil detach "${mount}" >/dev/null
else
  echo "No DMG is in release/. Run scripts/package-dmg.sh to create one."
fi
