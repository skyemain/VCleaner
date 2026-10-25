#!/bin/bash
# Checks the macOS sources for required files and for cleanup shortcuts that must never ship.
# This does not compile the app and does not claim a Mac build.
set -euo pipefail
cd "$(dirname "$0")/.."

required=(
  Clenzy.xcodeproj/project.pbxproj
  Clenzy/App/ClenzyApp.swift
  Clenzy/Core/CleaningEngine.swift
  Clenzy/Services/SafeDeletionService.swift
  Clenzy/Configuration/ProtectedPaths.swift
  Clenzy/Cleaners/Chrome/ChromeCleaner.swift
  Clenzy/Cleaners/Safari/SafariCleaner.swift
  Clenzy/Resources/Info.plist
  Clenzy/Resources/Clenzy.entitlements
  scripts/build.sh
  scripts/package-dmg.sh
  scripts/sign.sh
  scripts/notarize.sh
  scripts/verify-release.sh
)
for path in "${required[@]}"; do
  test -f "${path}" || { echo "missing ${path}" >&2; exit 1; }
done

if grep -R -n -E 'rm -rf|sudo |SIGKILL|forceTerminate|tccutil|csrutil' --include='*.swift' Clenzy ClenzyTests; then
  echo "error: a Swift file contains a forbidden cleanup or security bypass." >&2
  exit 1
fi

echo "Source layout looks complete. This check did not compile or package the app."
